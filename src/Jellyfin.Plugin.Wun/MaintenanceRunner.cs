using MediaBrowser.Model.Tasks;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using Jellyfin.Data.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using MediaBrowser.Common.Api;
using Wun.Core;
namespace Jellyfin.Plugin.Wun;

public sealed record CoverageObservation(bool Known,bool Complete,bool Current,DateTime? Missing);
public sealed class MaintenanceRunner(RequestStore store,ILibraryManager library,NativeSchedule schedule,IUserManager users)
{
 readonly SemaphoreSlim gate=new(1);
 public Retention Policy {get{var c=Plugin.Instance?.Configuration??new();return new(Math.Max(0,c.CompletedDays),Math.Max(0,c.CanceledDays),Math.Max(0,c.ReadDays));}}
 public async Task Run(bool cleanup,IProgress<double> progress,CancellationToken ct)
 {
  if(!await gate.WaitAsync(0,ct))throw new InvalidOperationException("WUN maintenance already running.");
  try{
   if(cleanup){ct.ThrowIfCancellationRequested();store.Cleanup(Policy,true,ct);progress.Report(100);return;}
   var cards=store.All();var done=0;var failures=0;
   foreach(var card in cards){ct.ThrowIfCancellationRequested();try{await Update(card,ct);}catch(OperationCanceledException){throw;}catch(RevisionConflictException){}catch{failures++;}progress.Report(++done*100d/Math.Max(1,cards.Length));}
   if(failures>0)throw new InvalidOperationException($"WUN: {failures} requests could not be checked; their states were preserved.");
  }finally{gate.Release();}
 }
 public async Task<CoverageObservation> Inspect(RequestCard card,CancellationToken ct,Guid? owner=null)
 {
  var user=owner.HasValue?(users.GetUserById(owner.Value)??throw new UnauthorizedAccessException()):null;
  var s=card.Selection;var series=s.Kind==MediaKind.Series;
  var items=library.GetItemsResult(new InternalItemsQuery{User=user,Recursive=true,IncludeItemTypes=[series?BaseItemKind.Series:BaseItemKind.Movie],HasAnyProviderId=new Dictionary<string,string>{{"Tmdb",s.TmdbId}},IsVirtualItem=false,EnableTotalRecordCount=false}).Items.Where(i=>user==null||i.IsVisibleStandalone(user)).ToArray();
  bool complete=!series&&items.Any(i=>!string.IsNullOrWhiteSpace(i.Path));
  if(complete)return new(true,true,true,null);
  DateTime? missing=null;bool known=!series;bool current=false;
  if(!series&&card.Monitor){var remoteMovie=await schedule.Get(int.Parse(s.TmdbId),null,ct,"Movie");if(remoteMovie==null)return new(false,false,false,null);var date=NativeSchedule.Date(remoteMovie,"ReleaseDate");if(date.HasValue&&date.Value.Date<=DateTime.UtcNow.Date)missing=date;}
  if(series){
   var owned=new HashSet<(int,int)>();
   foreach(var item in items){var episodes=library.GetItemsResult(new InternalItemsQuery{User=user,Recursive=true,AncestorIds=[item.Id],IncludeItemTypes=[BaseItemKind.Episode],IsVirtualItem=false,EnableTotalRecordCount=false}).Items.Where(i=>user==null||i.IsVisibleStandalone(user)).ToArray();
    foreach(var ep in episodes.Where(e=>!string.IsNullOrWhiteSpace(e.Path)&&e.ParentIndexNumber.HasValue&&e.IndexNumber.HasValue))for(var n=ep.IndexNumber!.Value;n<=Math.Min((ep as MediaBrowser.Controller.Entities.TV.Episode)?.IndexNumberEnd??ep.IndexNumber.Value,ep.IndexNumber.Value+100);n++)owned.Add((ep.ParentIndexNumber!.Value,n));
   }
   if(s.Episode.HasValue){complete=owned.Contains((s.Season!.Value,s.Episode.Value));known=true;if(complete)return new(true,true,true,null);}
   var remote=await schedule.Get(int.Parse(s.TmdbId),null,ct);if(remote==null)return new(false,false,false,null);
   var seasons=NativeSchedule.Rows(remote,"Seasons").Where(x=>s.Season.HasValue?NativeSchedule.Num(x,"SeasonNumber")==s.Season:NativeSchedule.Num(x,"SeasonNumber")>0).ToArray();
   if(seasons.Length==0||seasons.Length>40)return new(false,false,false,null);
   known=true;var expected=new HashSet<(int,int)>();var released=new HashSet<(int,int)>();
   foreach(var season in seasons){var sn=NativeSchedule.Num(season,"SeasonNumber");var data=await schedule.Get(int.Parse(s.TmdbId),sn,ct);var rows=NativeSchedule.Rows(data,"Episodes");if(data==null||rows.Length!=NativeSchedule.Num(season,"EpisodeCount"))return new(false,false,false,null);
    foreach(var ep in rows){var en=NativeSchedule.Num(ep,"EpisodeNumber");if(s.Episode.HasValue&&en!=s.Episode)continue;var date=NativeSchedule.Date(ep,"AirDate");if(!date.HasValue){known=false;continue;}expected.Add((sn,en));if(date.Value.Date<=DateTime.UtcNow.Date){released.Add((sn,en));if(!owned.Contains((sn,en))&&(!missing.HasValue||date<missing))missing=date;}}
   }
   var ended=NativeSchedule.Prop(remote,"Status")?.ToString() is "Ended" or "Canceled";
   current=known&&released.Count>0&&released.All(owned.Contains);
   complete=complete||(known&&expected.Count>0&&expected.SetEquals(released)&&expected.All(owned.Contains)&&(s.Season.HasValue||ended));
  }
  if(complete)return new(true,true,true,null);
  return new(known,complete,current,missing);
 }
 async Task Update(RequestCard card,CancellationToken ct)
 {
  if(card.Decision==Decision.Completed)return;
  var observed=await Inspect(card,ct);var known=observed.Known;var missing=observed.Missing;
  if(observed.Complete){store.Transition(card.Id,card.Revision,Decision.Completed,"Requested content is available in the catalog.");return;}
  if(!known||card.Decision is not(Decision.Approved or Decision.Deferred))return;
  var config=Plugin.Instance?.Configuration??new();
  var reason=RequestStore.ExpirationReason(card,known,store.ApprovedAt(card.Id),missing.HasValue?new DateTimeOffset(missing.Value):null,config.ApprovalDays,config.MissingReleaseDays,DateTimeOffset.UtcNow);
  if(reason!=null)store.Transition(card.Id,card.Revision,Decision.Canceled,reason);
 }
}
public abstract class WunTask(MaintenanceRunner runner,bool cleanup):IScheduledTask
{
 public string Name=>cleanup?"WUN cleanup":"WUN availability update";
 public string Key=>cleanup?"WUN.Cleanup":"WUN.Availability";
 public string Description=>cleanup?"Remove eligible WUN records according to configured retention. Disabled policies preserve records.":"Reconcile requests with catalog availability and optional expiration policies.";
 public string Category=>"WUN";
 public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()=>[new(){Type=TaskTriggerInfoType.IntervalTrigger,IntervalTicks=TimeSpan.FromHours(cleanup?24:6).Ticks}];
 public Task ExecuteAsync(IProgress<double> progress,CancellationToken ct)=>runner.Run(cleanup,progress,ct);
}
public sealed class AvailabilityTask(MaintenanceRunner runner):WunTask(runner,false);
public sealed class CleanupTask(MaintenanceRunner runner):WunTask(runner,true);
[ApiController][Authorize(Policy=Policies.RequiresElevation)][Route("WUN/v1/admin/maintenance")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MaintenanceController(RequestStore store,MaintenanceRunner runner):ControllerBase
{
 [HttpGet("preview")]public IActionResult Preview(int? completedDays=null,int? canceledDays=null,int? readDays=null){if(new[]{completedDays,canceledDays,readDays}.Any(v=>v is <0 or >36500))return BadRequest();return Ok(new{Candidates=store.Cleanup(new(completedDays??runner.Policy.CompletedDays,canceledDays??runner.Policy.CanceledDays,readDays??runner.Policy.ReadDays)),LastCleanup=store.LastCleanup()});}
}
