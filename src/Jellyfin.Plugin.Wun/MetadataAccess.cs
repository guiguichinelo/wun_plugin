using MediaBrowser.Controller.Providers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.Providers;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using System.Collections.Concurrent;
namespace Jellyfin.Plugin.Wun;
public sealed record SearchCard(string Kind,string TmdbId,string Title,int? Year,string? ImageUrl=null,string? ImdbId=null,string[]? Genres=null,float? Rating=null,CatalogPresence? Catalog=null,bool IsAnime=false,string? BackdropUrl=null)
{
 public long? RequestId {get;init;}
 public int SupportCount {get;init;}
 public bool Supporting {get;init;}
}
public sealed record SearchFilters(int? YearFrom=null,int? YearTo=null,string? Genres=null,double? MinRating=null,string Obtained="any");
public interface IMetadataAccess { Task<SearchCard[]> Search(Guid user,string query,string kind,CancellationToken ct,SearchFilters? filters=null); Task<BaseItem?> Resolve(Guid userId,string kind,string id,CancellationToken ct); Task<string?> Poster(Guid user,string kind,string id,CancellationToken ct)=>Task.FromResult<string?>(null); }
public sealed record UserSummary(Guid Id,string Name,bool HasImage,string? ImageTag);
public interface IUserAccess { bool Administrator(Guid id); Guid[] Administrators(); UserSummary? Summary(Guid id)=>null; }
public sealed class UserAccess(IUserManager users):IUserAccess
{
 public bool Administrator(Guid id)=>users.GetUserById(id) is {} user && users.GetUserDto(user).Policy?.IsAdministrator==true;
 public Guid[] Administrators()=>users.GetUsers().Where(u=>Administrator(u.Id)).Select(u=>u.Id).ToArray();
 public UserSummary? Summary(Guid id){var user=users.GetUserById(id);if(user==null)return null;var dto=users.GetUserDto(user);return new(id,dto.Name,!string.IsNullOrEmpty(dto.PrimaryImageTag),dto.PrimaryImageTag);}
}
public sealed class MetadataAccess(IProviderManager providers,IUserManager users,ICatalogLookup catalog,IServerConfigurationManager configuration):IMetadataAccess
{
 readonly SemaphoreSlim gate=new(2);
 readonly ConcurrentDictionary<string,(DateTimeOffset Until,BaseItem Item)> cache=new();
 readonly ConcurrentDictionary<string,(DateTimeOffset Until,string? Url)> posters=new();
 public async Task<string?> Poster(Guid user,string kind,string id,CancellationToken ct)
 {
  var item=await Resolve(user,kind,id,ct);if(item==null)return null;
  var key=kind+":"+id;if(posters.TryGetValue(key,out var hit)&&hit.Until>DateTimeOffset.UtcNow)return hit.Url;
  string? url=null;
  try{var images=await providers.GetAvailableRemoteImages(item,new RemoteImageQuery("TheMovieDb"){ImageType=ImageType.Primary},ct);url=images.Select(x=>x.Url).FirstOrDefault(value=>Uri.TryCreate(value,UriKind.Absolute,out var parsed)&&parsed.Scheme=="https"&&parsed.Host=="image.tmdb.org")?.Replace("/t/p/original/","/t/p/w185/",StringComparison.Ordinal);}
  catch(OperationCanceledException){throw;}catch(HttpRequestException){return null;}
  if(posters.Count>1000)posters.Clear();posters[key]=(DateTimeOffset.UtcNow.AddMinutes(15),url);return url;
 }
 public async Task<SearchCard[]> Search(Guid user,string query,string kind,CancellationToken ct,SearchFilters? filters=null)
 {
  if(query.Length<2||query.Length>150||!SearchRules.ValidKinds(kind))throw new ArgumentException();
  filters??=new();
  var tasks=SearchRules.SearchPlan(kind).Select(p=>SearchCategory(user,query,p.Kind,ct,filters,p.AnimeOnly)).ToArray();
  var groups=await Task.WhenAll(tasks);
  return groups.Length==1?groups[0]:SearchRules.Combine(groups[0],groups[1]);
 }

 private async Task<SearchCard[]> SearchCategory(Guid user,string query,string kind,CancellationToken ct,SearchFilters filters,bool animeOnly)
 {
  await gate.WaitAsync(ct);
  try {
   var identifier=SearchRules.Identifier(query);
   var ids=identifier.Provider==null?new Dictionary<string,string>():new Dictionary<string,string>{{identifier.Provider,identifier.Id!}};
   var year=filters.YearFrom==filters.YearTo?filters.YearFrom:null;
   var results=kind=="Movie"?await providers.GetRemoteSearchResults<Movie,MovieInfo>(new(){SearchInfo=new(){Name=identifier.Provider==null?query:null,Year=year,ProviderIds=ids,MetadataLanguage=configuration.Configuration.PreferredMetadataLanguage,MetadataCountryCode=configuration.Configuration.MetadataCountryCode},SearchProviderName="TheMovieDb"},ct):await providers.GetRemoteSearchResults<Series,SeriesInfo>(new(){SearchInfo=new(){Name=identifier.Provider==null?query:null,Year=year,ProviderIds=ids,MetadataLanguage=configuration.Configuration.PreferredMetadataLanguage,MetadataCountryCode=configuration.Configuration.MetadataCountryCode},SearchProviderName="TheMovieDb"},ct);
   var cards=new List<SearchCard>();
   foreach(var result in results.Take(40)){
    ct.ThrowIfCancellationRequested();
    if(!result.ProviderIds.TryGetValue("Tmdb",out var id)||cards.Any(c=>c.TmdbId==id))continue;
    var item=await Resolve(user,kind,id,ct);
    if(item==null)continue;
    var anime=SearchRules.IsAnime(item.Genres,item.Tags,item.ProductionLocations);
    if(animeOnly&&!anime)continue;
    item.ProviderIds.TryGetValue("Imdb",out var imdb);
    var imageUrl=Uri.TryCreate(result.ImageUrl,UriKind.Absolute,out var image)&&image.Scheme=="https"&&image.Host=="image.tmdb.org"?image.ToString().Replace("/t/p/original/","/t/p/w185/",StringComparison.Ordinal):null;
    var presence=catalog.Find(user,kind,item.ProviderIds);
    var card=new SearchCard(kind,id,item.Name,item.ProductionYear,imageUrl,SearchRules.ImdbId(imdb),item.Genres,item.CommunityRating,presence,anime);
    if(SearchRules.Matches(card,filters)){
     try{
      var images=await providers.GetAvailableRemoteImages(item,new RemoteImageQuery("TheMovieDb"){ImageType=ImageType.Backdrop},ct);
      var backdrop=images.Where(x=>x.Type==ImageType.Backdrop).Select(x=>x.Url).FirstOrDefault(url=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="image.tmdb.org");
      card=card with{BackdropUrl=backdrop?.Replace("/t/p/original/","/t/p/w1280/",StringComparison.Ordinal)};
     }catch(OperationCanceledException){throw;}catch(HttpRequestException){/* Artwork is optional. */}
     cards.Add(card);
    }
    if(cards.Count==12)break;
   }
   return cards.ToArray();
  }finally{gate.Release();}
 }
 public async Task<BaseItem?> Resolve(Guid userId,string kind,string id,CancellationToken ct)
 {
  var user=users.GetUserById(userId)??throw new UnauthorizedAccessException();
  var key=kind+":"+id+":"+configuration.Configuration.PreferredMetadataLanguage+":"+configuration.Configuration.MetadataCountryCode;
  if(cache.TryGetValue(key,out var cached)&&cached.Until>DateTimeOffset.UtcNow)return cached.Item.IsVisibleStandalone(user)?cached.Item:null;
  BaseItem? item=null;
  if(kind=="Movie"){
   var provider=providers.GetMetadataProviders<Movie>(new Movie(),new LibraryOptions()).OfType<IRemoteMetadataProvider<Movie,MovieInfo>>().FirstOrDefault(p=>p.Name=="TheMovieDb");
   if(provider!=null)item=(await provider.GetMetadata(new(){ProviderIds=new(){{"Tmdb",id}},MetadataLanguage=configuration.Configuration.PreferredMetadataLanguage,MetadataCountryCode=configuration.Configuration.MetadataCountryCode},ct)).Item;
  }else if(kind=="Series"){
   var provider=providers.GetMetadataProviders<Series>(new Series(),new LibraryOptions()).OfType<IRemoteMetadataProvider<Series,SeriesInfo>>().FirstOrDefault(p=>p.Name=="TheMovieDb");
   if(provider!=null)item=(await provider.GetMetadata(new(){ProviderIds=new(){{"Tmdb",id}},MetadataLanguage=configuration.Configuration.PreferredMetadataLanguage,MetadataCountryCode=configuration.Configuration.MetadataCountryCode},ct)).Item;
  }
  if(item!=null){if(cache.Count>1000)cache.Clear();cache[key]=(DateTimeOffset.UtcNow.AddMinutes(15),item);}
  return item!=null&&item.IsVisibleStandalone(user)?item:null;
 }
}
