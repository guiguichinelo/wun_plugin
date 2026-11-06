using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Controller.Library;
using MediaBrowser.Common.Api;
using Jellyfin.Data.Enums;
using Wun.Core;
namespace Jellyfin.Plugin.Wun;
public sealed record CreateBody(string Kind,string TmdbId,int? Season=null,int? Episode=null,bool ConfirmReopen=false);
public sealed record DecideBody(long Revision,Decision Decision,string Message="",string InternalNote="",DateTimeOffset? ReviewAt=null,bool? Monitor=null);
[ApiController][Authorize][ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)][Route("WUN/v1")]
public sealed class RequestsController(RequestStore store,IMetadataAccess metadata,IUserAccess users,ICatalogLookup catalog,MaintenanceRunner? maintenance=null):ControllerBase
{
 readonly Dictionary<(long Id,bool Administrative),Task<RequestView?>> presentations=new();
 Guid Owner=>Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value,out var id)&&id!=Guid.Empty?id:throw new UnauthorizedAccessException();
 [HttpGet("me")]public object Me()=>new{Administrator=users.Administrator(Owner),PageSize=Plugin.Instance?.Configuration.PageSize??30,AdministratorOrder=Plugin.Instance?.Configuration.AdministratorOrder??"popular"};
 [Authorize(Policy=Policies.RequiresElevation)][HttpGet("admin/metrics")]public IActionResult Metrics()=>Ok(store.Metrics());
 [HttpGet("search")]public async Task<IActionResult> Search(string query,string kind="All",int? yearFrom=null,int? yearTo=null,string? genres=null,double? minRating=null,string obtained="any",CancellationToken ct=default){var filters=new SearchFilters(yearFrom,yearTo,genres,minRating,obtained);if(string.IsNullOrWhiteSpace(query)||query.Length<2||query.Length>150||!SearchRules.ValidKinds(kind)||!SearchRules.Valid(filters))return BadRequest();var cards=await metadata.Search(Owner,query,kind,ct,filters);var result=new List<SearchCard>();foreach(var card in cards){if(Enum.TryParse<MediaKind>(card.Kind,out var media)){var existing=store.FindSelection(new(media,card.TmdbId,card.Title));if(existing!=null){var participation=store.Participation(existing.Id,Owner);result.Add(card with{RequestId=existing.Id,SupportCount=participation.SupportCount,Supporting=participation.Supporting});continue;}}result.Add(card);}return Ok(result);}
 [HttpGet("requests")]public Task<IActionResult> List(int start=0,int limit=30,bool mine=true,Decision? decision=null,string sort="newest",CancellationToken ct=default)=>ListCards(start,limit,decision,sort,true,false,ct);
 [Authorize(Policy=Policies.RequiresElevation)][HttpGet("admin/requests")]public Task<IActionResult> AdminList(int start=0,int limit=30,Decision? decision=null,string sort="popular",CancellationToken ct=default)=>ListCards(start,limit,decision,sort,false,true,ct);
 async Task<IActionResult> ListCards(int start,int limit,Decision? decision,string sort,bool mine,bool administrative,CancellationToken ct)
 {
  if(start<0||limit<1||limit>100||(decision.HasValue&&!Enum.IsDefined(decision.Value))||sort is not("newest" or "oldest" or "popular"))return BadRequest();
  Response.Headers.CacheControl="private, no-store";var page=store.List(start,limit,decision:decision,participant:mine?Owner:null,sort:sort);var visible=new List<object>();
  foreach(var card in page.Items){var projection=await Present(card,administrative,ct);if(projection!=null)visible.Add(projection);}
  return Ok(new{Items=visible,HasMore=start+limit<page.Total,Start=start,Limit=limit});
 }
 [HttpPost("requests")]public async Task<IActionResult> Create(CreateBody body,CancellationToken ct){if(!Enum.TryParse<MediaKind>(body.Kind,out var kind)||!Enum.IsDefined(kind))return BadRequest();var selection=new Selection(kind,body.TmdbId,"pending",body.Season,body.Episode);try{selection.Validate();}catch(ArgumentException){return BadRequest();}var item=await metadata.Resolve(Owner,body.Kind,body.TmdbId,ct);if(item==null)return NotFound();if(body.Kind=="Movie"||body.Episode.HasValue){var presence=catalog.Find(Owner,body.Kind,item.ProviderIds,body.Season,body.Episode);if(presence.Obtained)return Conflict(new{Error="already_obtained",Catalog=presence});}selection=selection with{Title=item.Name};var existing=store.FindSelection(selection);var settings=Plugin.Instance?.Configuration??new Configuration();
var anime=SearchRules.IsAnime(item.Genres,item.Tags,item.ProductionLocations);
if(!settings.AcceptRequests&&(existing==null||existing.Decision is Decision.Rejected or Decision.Canceled))return Conflict(new{Error="requests_paused"});
if(anime?!settings.AllowAnime:kind==MediaKind.Movie?!settings.AllowMovies:!settings.AllowSeries)return Conflict(new{Error="content_not_allowed"});
if((existing==null||existing.Decision is Decision.Rejected or Decision.Canceled&&store.Participation(existing.Id,Owner).OwnRequest)&&!users.Administrator(Owner)&&settings.ActiveLimit>0&&store.All().Count(c=>store.Participation(c.Id,Owner).OwnRequest&&c.Decision is Decision.Pending or Decision.Approved or Decision.Deferred)>=settings.ActiveLimit)return Conflict(new{Error="active_limit"});
if(existing?.Decision is Decision.Rejected or Decision.Canceled){if(!body.ConfirmReopen)return Conflict(new{Error="reopen_confirmation",Message=store.History(existing.Id).LastOrDefault()?.Message});store.Transition(existing.Id,existing.Revision,Decision.Pending,"Request reopened by confirmed user interest.",users.Administrators());}

var existed=store.FindSelection(selection)!=null;var card=store.Create(Owner,selection,users.Administrators());if(!settings.FollowByDefault&&existing==null)store.Follow(card.Id,Owner,false);var result=await Present(card,false,ct);return Ok(result! with{ExistingRequest=existed});}
 Task<RequestView?> Present(RequestCard card,bool administrative,CancellationToken ct)
 {
  var key=(card.Id,administrative);
  if(!presentations.TryGetValue(key,out var task))presentations[key]=task=PresentCurrent(card,administrative,ct);
  return task;
 }
 async Task<RequestView?> PresentCurrent(RequestCard card,bool administrative,CancellationToken ct)
 {
  var item=await metadata.Resolve(Owner,card.Selection.Kind.ToString(),card.Selection.TmdbId,ct);if(item==null)return null;
  var presence=catalog.Find(Owner,card.Selection.Kind.ToString(),item.ProviderIds,card.Selection.Season,card.Selection.Episode);
  var state=presence.Obtained?(card.Selection.Kind==MediaKind.Movie||card.Selection.Episode.HasValue?Availability.Available:Availability.Partial):Availability.Absent;
  CoverageObservation? coverage=null;if(maintenance!=null){try{coverage=await maintenance.Inspect(card,ct,Owner);if(coverage.Known)state=coverage.Complete?Availability.Available:presence.Obtained?Availability.Partial:Availability.Absent;}catch(Exception e)when(e is not OperationCanceledException){}}
  item.ProviderIds.TryGetValue("Imdb",out var imdb);var activity=store.Participation(card.Id,Owner);
  var result=new RequestView(card.Id,card.Selection,card.Decision,state,card.Monitor,card.Revision,presence,SearchRules.ImdbId(imdb),item.ProductionYear,await metadata.Poster(Owner,card.Selection.Kind.ToString(),card.Selection.TmdbId,ct),activity.SupportCount,activity.Supporting,activity.Following,activity.OwnRequest,activity.Created){Current=coverage?.Current??false,IsAnime=SearchRules.IsAnime(item.Genres,item.Tags,item.ProductionLocations)};
  if(administrative){var data=store.Administrative(card.Id);result=result with{Requester=data.Requester.HasValue?users.Summary(data.Requester.Value):null,RequesterKnown=data.Requester.HasValue,Supporters=data.Supporters.Select(id=>users.Summary(id)).OfType<UserSummary>().ToArray(),LastDecision=data.LastDecision};}
  return result;
 }
 [HttpGet("requests/{id:long}")]public Task<IActionResult> One(long id,CancellationToken ct)=>GetOne(id,false,ct);
 [Authorize(Policy=Policies.RequiresElevation)][HttpGet("admin/requests/{id:long}")]public Task<IActionResult> AdminOne(long id,CancellationToken ct)=>GetOne(id,true,ct);
 async Task<IActionResult> GetOne(long id,bool administrative,CancellationToken ct){try{var result=await Present(store.Get(id),administrative,ct);return result==null?NotFound():Ok(result);}catch(KeyNotFoundException){return NotFound();}}
 async Task<bool> Visible(long id,CancellationToken ct){try{var c=store.Get(id);return await metadata.Resolve(Owner,c.Selection.Kind.ToString(),c.Selection.TmdbId,ct)!=null;}catch(KeyNotFoundException){return false;}}
 [HttpPut("requests/{id:long}/follow")]public async Task<IActionResult> Follow(long id,bool enabled=true,CancellationToken ct=default){if(!await Visible(id,ct))return NotFound();store.Follow(id,Owner,enabled);return Ok(new{Following=enabled});}
 [HttpPut("requests/{id:long}/support")]public async Task<IActionResult> Support(long id,bool enabled=true,CancellationToken ct=default){if(!await Visible(id,ct))return NotFound();if(enabled&&store.Get(id).Decision is Decision.Rejected or Decision.Canceled)return Conflict(new{Error="reopen_confirmation"});var previous=store.Participation(id,Owner);store.Support(id,Owner,enabled);if(enabled&&!previous.Supporting)store.DefaultFollow(id,Owner,Plugin.Instance?.Configuration.FollowByDefault??false);return Ok(store.Participation(id,Owner));}
 [HttpGet("requests/{id:long}/history")]public async Task<IActionResult> History(long id,CancellationToken ct){if(!await Visible(id,ct))return NotFound();return Ok(store.History(id));}
 [Authorize(Policy=Policies.RequiresElevation)][HttpGet("admin/requests/{id:long}/history")]public IActionResult AdminHistory(long id)=>Ok(store.AdminHistory(id));
 [Authorize(Policy=Policies.RequiresElevation)][HttpPut("admin/requests/{id:long}")]public IActionResult Decide(long id,DecideBody body){try{store.Decide(id,body.Revision,Owner,body.Decision,body.Message,body.InternalNote,body.ReviewAt,body.Monitor);return Ok(new{Saved=true});}catch(ArgumentException){return BadRequest();}catch(RevisionConflictException){return Conflict(new{error="revision_conflict"});}}
 [HttpGet("notifications")]public async Task<IActionResult> Notifications(int start=0,int limit=30,bool? read=null,CancellationToken ct=default){if(start<0||limit<1||limit>100)return BadRequest();var result=new List<object>();foreach(var n in store.WorkflowNotifications(Owner,start,limit,read)){var card=await Present(store.Get(n.RequestId),false,ct);if(card==null)continue;result.Add(new{n.Id,n.RequestId,n.Event,n.Read,n.Created,n.Message,Request=card,Actor=users.Administrator(Owner)&&n.Actor.HasValue?users.Summary(n.Actor.Value):null});}return Ok(result);}
 [HttpPost("notifications/{id:long}/read")]public IActionResult ReadOne(long id)=>store.MarkNotificationRead(Owner,id)?Ok(new{Read=true}):NotFound();
 [HttpPost("notifications/read")]public IActionResult Read(){store.MarkNotificationsRead(Owner);return Ok(new{Read=true});}
}
[ApiController][Route("WUN/web")]
public sealed class WebController:ControllerBase
{
 [AllowAnonymous][HttpGet("{file}")]public IActionResult Resource(string file){if(file is not("requests.html" or "app.js" or "style.css" or "header.js"))return NotFound();var stream=typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Wun.Web."+file);return stream==null?NotFound():File(stream,file.EndsWith("js")?"application/javascript":file.EndsWith("css")?"text/css":"text/html; charset=utf-8");}
}
