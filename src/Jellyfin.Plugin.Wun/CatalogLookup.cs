using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using Jellyfin.Data.Enums;
namespace Jellyfin.Plugin.Wun;
public sealed record CatalogPresence(bool Obtained,Guid? ItemId=null,int? EpisodeCount=null);
public interface ICatalogLookup { CatalogPresence Find(Guid owner,string kind,IReadOnlyDictionary<string,string> ids,int? season=null,int? episode=null); }
public sealed class CatalogLookup(ILibraryManager library,IUserManager users,LocalReadCache? reads=null):ICatalogLookup
{
 public CatalogPresence Find(Guid owner,string kind,IReadOnlyDictionary<string,string> ids,int? season=null,int? episode=null)
 {
  var key=kind+":"+season+":"+episode+":"+string.Join(";",ids.Where(x=>x.Key is "Tmdb" or "Imdb").OrderBy(x=>x.Key).Select(x=>x.Key+"="+x.Value));
  return reads==null?FindCurrent(owner,kind,ids,season,episode):reads.Get(reads.Scope(owner),key,()=>FindCurrent(owner,kind,ids,season,episode));
 }
 CatalogPresence FindCurrent(Guid owner,string kind,IReadOnlyDictionary<string,string> ids,int? season,int? episode)
 {
  var user=users.GetUserById(owner)??throw new UnauthorizedAccessException();
  var keys=ids.Where(x=>x.Key is "Tmdb" or "Imdb").ToDictionary(x=>x.Key,x=>x.Value);
  if(keys.Count==0)return new(false);
  var candidates=library.GetItemsResult(new InternalItemsQuery{User=user,Recursive=true,IncludeItemTypes=[kind=="Movie"?BaseItemKind.Movie:BaseItemKind.Series],HasAnyProviderId=keys,IsVirtualItem=false,EnableTotalRecordCount=false}).Items;
  foreach(var item in candidates){
   if(!item.IsVisibleStandalone(user))continue;
   if(kind=="Movie"){if(!string.IsNullOrWhiteSpace(item.Path))return new(true,item.Id);continue;}
   var episodes=library.GetItemsResult(new InternalItemsQuery{User=user,Recursive=true,AncestorIds=[item.Id],IncludeItemTypes=[BaseItemKind.Episode],IsVirtualItem=false,EnableTotalRecordCount=true,Limit=1,ParentIndexNumber=season,IndexNumber=episode});
   if(episodes.TotalRecordCount>0)return new(true,item.Id,episodes.TotalRecordCount);
  }
  return new(false);
 }
}
