using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Configuration;
using Jellyfin.Data.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace Jellyfin.Plugin.Wun;

public static class TitleCoverage
{
 public static (bool Released,bool Complete) Evaluate(HashSet<(int,int)> expected,HashSet<(int,int)> released,IReadOnlyDictionary<(int,int),Guid> owned,bool known,bool ended)
 {
  var current=known&&released.Count>0&&released.All(owned.ContainsKey);
  return(current,current&&ended&&expected.All(owned.ContainsKey));
 }
}

// Uses the public TMDb client manager supplied by the installed Jellyfin provider.
// No provider credentials or account-dependent catalog data enter this cache.
public sealed class NativeSchedule(IServiceProvider services,IServerConfigurationManager config):IDisposable
{
 private readonly BoundedAsyncCache<object?> cache=new(200);
 private readonly SemaphoreSlim gate=new(3);
 private Type? clientType;
 public Task<object?> Get(int id,int? season,CancellationToken ct,string kind="Series")
 {
  var language=config.Configuration.PreferredMetadataLanguage;var country=config.Configuration.MetadataCountryCode;
  var key=$"{kind}:{id}:{season}:{language}:{country}";
  return cache.Get(key,TimeSpan.FromMinutes(15),token=>Load(id,season,kind,language,country,token),ct);
 }
 private async Task<object?> Load(int id,int? season,string kind,string? language,string? country,CancellationToken ct)
 {
  await gate.WaitAsync(ct);
  try{
   var type=clientType??=AssemblyLoadContext.All.SelectMany(c=>c.Assemblies).SelectMany(a=>a.GetExportedTypes()).FirstOrDefault(t=>t.Name=="TmdbClientManager");
   var client=type==null?null:services.GetService(type);if(client==null)return null;
   var method=type!.GetMethods().FirstOrDefault(m=>m.Name==(season.HasValue?"GetSeasonAsync":kind=="Movie"?"GetMovieAsync":"GetSeriesAsync")&&m.GetParameters().Length==(season.HasValue?6:5));
   if(method==null)return null;
   var args=new List<object?>{id};if(season.HasValue)args.Add(season.Value);
   args.AddRange([language,null,country,ct]);
   var task=(Task)method.Invoke(client,args.ToArray())!;await task;
   return task.GetType().GetProperty("Result")?.GetValue(task);
  }finally{gate.Release();}
 }
 public void Dispose(){cache.Dispose();gate.Dispose();}
 public static object? Prop(object? o,string name)=>o?.GetType().GetProperty(name)?.GetValue(o);
 public static int Num(object? o,string name)=>Convert.ToInt32(Prop(o,name)??0);
 public static object[] Rows(object? o,string name)=>(Prop(o,name) as IEnumerable)?.Cast<object>().ToArray()??[];
 public static DateTime? Date(object? o,string name)=>Prop(o,name) is DateTime d?DateTime.SpecifyKind(d,DateTimeKind.Utc):null;
 public static string? Still(object ep)
 {
  var path=Prop(ep,"StillPath") as string;
  return path!=null&&System.Text.RegularExpressions.Regex.IsMatch(path,@"^/[A-Za-z0-9_-]+\.(jpg|png|webp)$")?"https://image.tmdb.org/t/p/w300"+path:null;
 }
 public static string? Poster(object? item)
 {
  var path=Prop(item,"PosterPath") as string;
  return path!=null&&System.Text.RegularExpressions.Regex.IsMatch(path,@"^/[A-Za-z0-9_-]+\.(jpg|png|webp)$")?"https://image.tmdb.org/t/p/w342"+path:null;
 }
}

[ApiController][Authorize][Route("WUN/v1")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TitleDetailsController(IMetadataAccess metadata,ICatalogLookup catalog,ILibraryManager library,IUserManager users,NativeSchedule schedule):ControllerBase
{
 [HttpGet("details")]
 public async Task<IActionResult> Get(string kind,string tmdbId,int? season=null,CancellationToken ct=default)
 {
  if(kind is not ("Movie" or "Series")||!int.TryParse(tmdbId,out var number)||number<1||season is <0 or >1000)return BadRequest();
  if(!Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value,out var owner))return Unauthorized();
  var item=await metadata.Resolve(owner,kind,tmdbId,ct);if(item==null)return NotFound();
  var presence=catalog.Find(owner,kind,item.ProviderIds);
  if(kind=="Movie"){
   object? movie=null;try{movie=await schedule.Get(number,null,ct,"Movie");}catch(Exception ex)when(ex is not OperationCanceledException){}
   return Ok(new{Title=item.Name,item.Overview,Kind=kind,ImageUrl=NativeSchedule.Poster(movie),Catalog=presence,Year=item.ProductionYear,RuntimeMinutes=item.RunTimeTicks/TimeSpan.TicksPerMinute});
  }
  var user=users.GetUserById(owner)??throw new UnauthorizedAccessException();
  var owned=presence.ItemId.HasValue?library.GetItemsResult(new InternalItemsQuery{User=user,AncestorIds=[presence.ItemId.Value],Recursive=true,IncludeItemTypes=[BaseItemKind.Episode],IsVirtualItem=false,EnableTotalRecordCount=false}).Items.Where(e=>e.IsVisibleStandalone(user)&&!string.IsNullOrWhiteSpace(e.Path)).ToArray():[];
  var index=new Dictionary<(int,int),Guid>();
  foreach(var ep in owned)if(ep.ParentIndexNumber.HasValue&&ep.IndexNumber.HasValue){var last=Math.Min((ep as MediaBrowser.Controller.Entities.TV.Episode)?.IndexNumberEnd??ep.IndexNumber.Value,ep.IndexNumber.Value+100);for(var n=ep.IndexNumber.Value;n<=last;n++)index.TryAdd((ep.ParentIndexNumber.Value,n),ep.Id);}
  object? remote=null;try{remote=await schedule.Get(number,null,ct);}catch(Exception ex)when(ex is not OperationCanceledException){/* Unknown coverage is never reported complete. */}
  var seasons=NativeSchedule.Rows(remote,"Seasons").Where(s=>NativeSchedule.Num(s,"SeasonNumber")>=0).OrderBy(s=>NativeSchedule.Num(s,"SeasonNumber")).ToArray();
  var selected=season??seasons.Select(s=>NativeSchedule.Num(s,"SeasonNumber")).FirstOrDefault(n=>n>0,1);
  var episodes=new List<object>();var expectedMain=new HashSet<(int,int)>();var releasedMain=new HashSet<(int,int)>();bool known=remote!=null&&seasons.Length>0&&seasons.Length<=40;int? future=0;
  foreach(var s in seasons.Take(40).Concat(seasons.Where(s=>NativeSchedule.Num(s,"SeasonNumber")==selected)).Distinct()){
   var sn=NativeSchedule.Num(s,"SeasonNumber");object? detail=null;
   try{detail=await schedule.Get(number,sn,ct);}catch(Exception ex)when(ex is not OperationCanceledException){known=false;}
   var rows=NativeSchedule.Rows(detail,"Episodes");if(detail==null||rows.Length!=NativeSchedule.Num(s,"EpisodeCount"))known=false;
   foreach(var ep in rows){
    var en=NativeSchedule.Num(ep,"EpisodeNumber");var date=NativeSchedule.Date(ep,"AirDate");bool released=date.HasValue&&date.Value.Date<=DateTime.UtcNow.Date;
    if(sn>0){expectedMain.Add((sn,en));if(released)releasedMain.Add((sn,en));else if(date.HasValue)future++;else known=false;}
    if(sn==selected){index.TryGetValue((sn,en),out var nativeId);episodes.Add(new{Number=en,Title=NativeSchedule.Prop(ep,"Name"),Overview=NativeSchedule.Prop(ep,"Overview"),ImageUrl=NativeSchedule.Still(ep),AirDate=date,Available=nativeId!=Guid.Empty,ItemId=nativeId==Guid.Empty?(Guid?)null:nativeId,Released=released});}
   }
  }
  var status=NativeSchedule.Prop(remote,"Status")?.ToString()??"Unknown";
  var ended=status is "Ended" or "Canceled";
  var coverage=TitleCoverage.Evaluate(expectedMain,releasedMain,index,known,ended);
  return Ok(new{Title=item.Name,item.Overview,Kind=kind,ImageUrl=NativeSchedule.Poster(remote),Year=item.ProductionYear,Status=status,Ended=ended,Catalog=presence,ProviderKnown=known,CompleteReleased=coverage.Released,Complete=coverage.Complete,Owned=releasedMain.Count(index.ContainsKey),Released=releasedMain.Count,Expected=expectedMain.Count,Future=future,SelectedSeason=selected,Seasons=seasons.Select(s=>new{Number=NativeSchedule.Num(s,"SeasonNumber"),Title=NativeSchedule.Prop(s,"Name"),ImageUrl=NativeSchedule.Poster(s),Expected=NativeSchedule.Num(s,"EpisodeCount")}),Episodes=episodes});
 }
}
