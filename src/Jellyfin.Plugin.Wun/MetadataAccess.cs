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
public sealed class MetadataAccess(IProviderManager providers,IUserManager users,ICatalogLookup catalog,IServerConfigurationManager configuration):IMetadataAccess,IDisposable
{
 readonly SemaphoreSlim gate=new(2);
 readonly BoundedAsyncCache<BaseItem?> metadata=new(1000);
 readonly BoundedAsyncCache<RemoteSearchResult[]> searches=new(256);
 readonly BoundedAsyncCache<string?> artwork=new(1000);
 string Locale=>configuration.Configuration.PreferredMetadataLanguage+":"+configuration.Configuration.MetadataCountryCode;
 async Task<T> Provider<T>(Func<Task<T>> load,CancellationToken ct){await gate.WaitAsync(ct);try{return await load();}finally{gate.Release();}}
 async Task<string?> Image(BaseItem item,string kind,string id,ImageType type,CancellationToken ct)
 {
  var key=kind+":"+id+":"+type+":"+Locale;
  return await artwork.Get(key,TimeSpan.FromMinutes(30),token=>Provider(async()=>{
   var images=await providers.GetAvailableRemoteImages(item,new RemoteImageQuery("TheMovieDb"){ImageType=type},token);
   return images.Where(x=>x.Type==type).Select(x=>x.Url).FirstOrDefault(value=>Uri.TryCreate(value,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&uri.Host=="image.tmdb.org");
  },token),ct);
 }
 public async Task<string?> Poster(Guid user,string kind,string id,CancellationToken ct)
 {
  var item=await Resolve(user,kind,id,ct);if(item==null)return null;
  try{return (await Image(item,kind,id,ImageType.Primary,ct))?.Replace("/t/p/original/","/t/p/w185/",StringComparison.Ordinal);}
  catch(OperationCanceledException){throw;}catch(HttpRequestException){return null;}
 }
 public async Task<SearchCard[]> Search(Guid user,string query,string kind,CancellationToken ct,SearchFilters? filters=null)
 {
  if(query.Length<2||query.Length>150||!SearchRules.ValidKinds(kind))throw new ArgumentException();
  filters??=new();
  var tasks=SearchRules.SearchPlan(kind).Select(p=>SearchCategory(user,query,p.Kind,ct,filters,p.AnimeOnly)).ToArray();
  var groups=await Task.WhenAll(tasks);
  return groups.Length==1?groups[0]:SearchRules.Combine(groups[0],groups[1]);
 }
 async Task<RemoteSearchResult[]> SearchRemote(string query,string kind,int? year,CancellationToken ct)
 {
  var language=configuration.Configuration.PreferredMetadataLanguage;var country=configuration.Configuration.MetadataCountryCode;
  var key=System.Text.Json.JsonSerializer.Serialize(new{query=query.Trim(),kind,year,language,country});
  return await searches.Get(key,TimeSpan.FromMinutes(15),token=>Provider(async()=>{
   var identifier=SearchRules.Identifier(query);
   var ids=identifier.Provider==null?new Dictionary<string,string>():new Dictionary<string,string>{{identifier.Provider,identifier.Id!}};
   var results=kind=="Movie"?await providers.GetRemoteSearchResults<Movie,MovieInfo>(new(){SearchInfo=new(){Name=identifier.Provider==null?query:null,Year=year,ProviderIds=ids,MetadataLanguage=language,MetadataCountryCode=country},SearchProviderName="TheMovieDb"},token):await providers.GetRemoteSearchResults<Series,SeriesInfo>(new(){SearchInfo=new(){Name=identifier.Provider==null?query:null,Year=year,ProviderIds=ids,MetadataLanguage=language,MetadataCountryCode=country},SearchProviderName="TheMovieDb"},token);
   return results.Take(40).ToArray();
  },token),ct);
 }
 private async Task<SearchCard[]> SearchCategory(Guid user,string query,string kind,CancellationToken ct,SearchFilters filters,bool animeOnly)
 {
  var year=filters.YearFrom==filters.YearTo?filters.YearFrom:null;
  var results=await SearchRemote(query,kind,year,ct);var cards=new List<SearchCard>();
  foreach(var result in results){
   ct.ThrowIfCancellationRequested();
   if(!result.ProviderIds.TryGetValue("Tmdb",out var id)||cards.Any(c=>c.TmdbId==id))continue;
   var item=await Resolve(user,kind,id,ct);if(item==null)continue;
   var anime=SearchRules.IsAnime(item.Genres,item.Tags,item.ProductionLocations);if(animeOnly&&!anime)continue;
   item.ProviderIds.TryGetValue("Imdb",out var imdb);
   var imageUrl=Uri.TryCreate(result.ImageUrl,UriKind.Absolute,out var image)&&image.Scheme=="https"&&image.Host=="image.tmdb.org"?image.ToString().Replace("/t/p/original/","/t/p/w185/",StringComparison.Ordinal):null;
   var card=new SearchCard(kind,id,item.Name,item.ProductionYear,imageUrl,SearchRules.ImdbId(imdb),item.Genres,item.CommunityRating,catalog.Find(user,kind,item.ProviderIds),anime);
   if(SearchRules.Matches(card,filters)){
    try{card=card with{BackdropUrl=(await Image(item,kind,id,ImageType.Backdrop,ct))?.Replace("/t/p/original/","/t/p/w1280/",StringComparison.Ordinal)};}
    catch(OperationCanceledException){throw;}catch(HttpRequestException){/* Optional artwork is retried rather than cached as a failure. */}
    cards.Add(card);
   }
   if(cards.Count==12)break;
  }
  return cards.ToArray();
 }
 public async Task<BaseItem?> Resolve(Guid userId,string kind,string id,CancellationToken ct)
 {
  var user=users.GetUserById(userId)??throw new UnauthorizedAccessException();
  var language=configuration.Configuration.PreferredMetadataLanguage;var country=configuration.Configuration.MetadataCountryCode;
  var key=kind+":"+id+":"+language+":"+country;
  var item=await metadata.Get(key,TimeSpan.FromMinutes(15),token=>Provider(async()=>{
   if(kind=="Movie"){
    var provider=providers.GetMetadataProviders<Movie>(new Movie(),new LibraryOptions()).OfType<IRemoteMetadataProvider<Movie,MovieInfo>>().FirstOrDefault(p=>p.Name=="TheMovieDb");
    return provider==null?null:(await provider.GetMetadata(new(){ProviderIds=new(){{"Tmdb",id}},MetadataLanguage=language,MetadataCountryCode=country},token)).Item;
   }
   var seriesProvider=providers.GetMetadataProviders<Series>(new Series(),new LibraryOptions()).OfType<IRemoteMetadataProvider<Series,SeriesInfo>>().FirstOrDefault(p=>p.Name=="TheMovieDb");
   return seriesProvider==null?(BaseItem?)null:(await seriesProvider.GetMetadata(new(){ProviderIds=new(){{"Tmdb",id}},MetadataLanguage=language,MetadataCountryCode=country},token)).Item;
  },token),ct);
  // Never share the visibility result: evaluate the current user on every cache hit.
  return item!=null&&item.IsVisibleStandalone(user)?item:null;
 }
 public void Dispose(){metadata.Dispose();searches.Dispose();artwork.Dispose();gate.Dispose();}
}
