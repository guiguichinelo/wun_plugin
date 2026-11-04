using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Controller.Library;
using MediaBrowser.Common.Api;
using Jellyfin.Data.Enums;
using Wun.Core;
namespace Jellyfin.Plugin.Wun;
public sealed record CreateBody(string Kind,string TmdbId,int? Season=null,int? Episode=null);
public sealed record DecideBody(long Revision,Decision Decision,string Message="",string InternalNote="",DateTimeOffset? ReviewAt=null,bool? Monitor=null);
[ApiController][Authorize][ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)][Route("WUN/v1")]
public sealed class RequestsController(RequestStore store,IMetadataAccess metadata,IUserAccess users,ICatalogLookup catalog):ControllerBase
{
 Guid Owner=>Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value,out var id)&&id!=Guid.Empty?id:throw new UnauthorizedAccessException();
 [HttpGet("me")]public object Me()=>new{Administrator=users.Administrator(Owner)};
 [HttpGet("search")]public async Task<IActionResult> Search(string query,string kind="All",int? yearFrom=null,int? yearTo=null,string? genres=null,double? minRating=null,string obtained="any",CancellationToken ct=default){var filters=new SearchFilters(yearFrom,yearTo,genres,minRating,obtained);if(string.IsNullOrWhiteSpace(query)||query.Length<2||query.Length>150||!SearchRules.ValidKinds(kind)||!SearchRules.Valid(filters))return BadRequest();return Ok(await metadata.Search(Owner,query,kind,ct,filters));}
 [HttpGet("requests")]public async Task<IActionResult> List(int start=0,int limit=30,bool mine=false,Decision? decision=null,CancellationToken ct=default)
 {
  if(start<0||limit<1||limit>100||(decision.HasValue&&!Enum.IsDefined(decision.Value)))return BadRequest();
  Response.Headers.CacheControl="private, no-store";var page=store.List(start,limit,mine?Owner:null,decision);var visible=new List<object>();
  foreach(var card in page.Items){var item=await metadata.Resolve(Owner,card.Selection.Kind.ToString(),card.Selection.TmdbId,ct);if(item==null)continue;
   var presence=catalog.Find(Owner,card.Selection.Kind.ToString(),item.ProviderIds,card.Selection.Season,card.Selection.Episode);
   var state=presence.Obtained?(card.Selection.Kind==MediaKind.Movie||card.Selection.Episode.HasValue?Availability.Available:Availability.Partial):Availability.Absent;
   item.ProviderIds.TryGetValue("Imdb",out var imdb);
   visible.Add(new{card.Id,card.Selection,card.Decision,Availability=state,card.Monitor,card.Revision,Catalog=presence,ImdbId=SearchRules.ImdbId(imdb)});
  }
  return Ok(new{Items=visible,HasMore=start+limit<page.Total,Start=start,Limit=limit});
 }
 [HttpPost("requests")]public async Task<IActionResult> Create(CreateBody body,CancellationToken ct){if(!Enum.TryParse<MediaKind>(body.Kind,out var kind)||!Enum.IsDefined(kind))return BadRequest();var selection=new Selection(kind,body.TmdbId,"pending",body.Season,body.Episode);try{selection.Validate();}catch(ArgumentException){return BadRequest();}var item=await metadata.Resolve(Owner,body.Kind,body.TmdbId,ct);if(item==null)return NotFound();if(body.Kind=="Movie"||body.Episode.HasValue){var presence=catalog.Find(Owner,body.Kind,item.ProviderIds,body.Season,body.Episode);if(presence.Obtained)return Conflict(new{Error="already_obtained",Catalog=presence});}selection=selection with{Title=item.Name};return Ok(store.Create(Owner,selection,users.Administrators()));}
 async Task<bool> Visible(long id,CancellationToken ct){try{var c=store.Get(id);return await metadata.Resolve(Owner,c.Selection.Kind.ToString(),c.Selection.TmdbId,ct)!=null;}catch(KeyNotFoundException){return false;}}
 [HttpPut("requests/{id:long}/follow")]public async Task<IActionResult> Follow(long id,bool enabled=true,CancellationToken ct=default){if(!await Visible(id,ct))return NotFound();store.Follow(id,Owner,enabled);return Ok(new{Following=enabled});}
 [HttpGet("requests/{id:long}/history")]public async Task<IActionResult> History(long id,CancellationToken ct){if(!await Visible(id,ct))return NotFound();return Ok(store.History(id));}
 [Authorize(Policy=Policies.RequiresElevation)][HttpGet("admin/requests/{id:long}/history")]public IActionResult AdminHistory(long id)=>Ok(store.AdminHistory(id));
 [Authorize(Policy=Policies.RequiresElevation)][HttpPut("admin/requests/{id:long}")]public IActionResult Decide(long id,DecideBody body){try{store.Decide(id,body.Revision,Owner,body.Decision,body.Message,body.InternalNote,body.ReviewAt,body.Monitor);return Ok(new{Saved=true});}catch(ArgumentException){return BadRequest();}catch(RevisionConflictException){return Conflict(new{error="revision_conflict"});}}
 [HttpGet("notifications")]public async Task<IActionResult> Notifications(CancellationToken ct){var result=new List<Notification>();foreach(var n in store.Notifications(Owner))if(await Visible(n.RequestId,ct))result.Add(n);return Ok(result);}
 [HttpPost("notifications/read")]public IActionResult Read(){store.MarkNotificationsRead(Owner);return Ok(new{Read=true});}
}
[ApiController][Route("WUN/web")]
public sealed class WebController:ControllerBase
{
 [AllowAnonymous][HttpGet("{file}")]public IActionResult Resource(string file){if(file is not("requests.html" or "app.js" or "style.css" or "header.js"))return NotFound();var stream=typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Wun.Web."+file);return stream==null?NotFound():File(stream,file.EndsWith("js")?"application/javascript":file.EndsWith("css")?"text/css":"text/html; charset=utf-8");}
}
