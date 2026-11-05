using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Wun.Core;
namespace Jellyfin.Plugin.Wun;
public sealed class Configuration:BasePluginConfiguration
{
 private int pageSize=30;
 private string order="popular";
 public bool AcceptRequests {get;set;}=true;
 private int activeLimit;
 public int ActiveLimit {get=>activeLimit;set=>activeLimit=Math.Clamp(value,0,36500);}
 public bool AllowMovies {get;set;}=true;
 public bool AllowSeries {get;set;}=true;
 public bool AllowAnime {get;set;}=true;
 public bool FollowByDefault {get;set;}=true;
 private int approvalDays;
 public int ApprovalDays {get=>approvalDays;set=>approvalDays=Math.Clamp(value,0,36500);}
 private int missingReleaseDays;
 public int MissingReleaseDays {get=>missingReleaseDays;set=>missingReleaseDays=Math.Clamp(value,0,36500);}
 private int completedDays;
 public int CompletedDays {get=>completedDays;set=>completedDays=Math.Clamp(value,0,36500);}
 private int canceledDays;
 public int CanceledDays {get=>canceledDays;set=>canceledDays=Math.Clamp(value,0,36500);}
 private int readDays;
 public int ReadDays {get=>readDays;set=>readDays=Math.Clamp(value,0,36500);}
 public int PageSize {get=>pageSize;set=>pageSize=Math.Clamp(value,10,100);}
 public string AdministratorOrder {get=>order;set=>order=value is "newest" or "oldest"?value:"popular";}
}
public sealed class Plugin:BasePlugin<Configuration>,IHasWebPages
{
 public Plugin(IApplicationPaths paths,IXmlSerializer serializer):base(paths,serializer){Instance=this;}
 public static Plugin? Instance {get;private set;}
 public override string Name=>"What U Need?";
 public override Guid Id=>Guid.Parse("1d91696c-0fdd-46da-8734-3a3d94e33002");
 public override string Description=>"Community catalog requests. Public beta for Jellyfin 12.4.";
 public IEnumerable<PluginPageInfo> GetPages()=>[new(){Name="wun",DisplayName="WUN",EmbeddedResourcePath="Jellyfin.Plugin.Wun.Web.config.html",EnableInMainMenu=true,MenuSection="server",MenuIcon="room_service"}];
}
public sealed class Registrator:IPluginServiceRegistrator
{
 public void RegisterServices(IServiceCollection services,IServerApplicationHost host)
 {
  services.AddSingleton<RequestStore>(sp=>new(Path.Combine(sp.GetRequiredService<IApplicationPaths>().DataPath,"wun","wun.db")));
  services.AddSingleton<MaintenanceRunner>();services.AddSingleton<MediaBrowser.Model.Tasks.IScheduledTask,AvailabilityTask>();services.AddSingleton<MediaBrowser.Model.Tasks.IScheduledTask,CleanupTask>();
  services.AddSingleton<NativeSchedule>();services.AddSingleton<ICatalogLookup,CatalogLookup>();services.AddSingleton<IMetadataAccess,MetadataAccess>();services.AddSingleton<IUserAccess,UserAccess>();services.AddHostedService<PageRegistration>();services.AddHostedService<NavigationRegistration>();
 }
}
public sealed class PageRegistration(ILogger<PageRegistration> logger):IHostedService
{
 public Task StartAsync(CancellationToken ct)
 {
  try {
   var type=AssemblyLoadContext.All.SelectMany(c=>c.Assemblies).Select(a=>a.GetType("Jellyfin.Plugin.PluginPages.PluginInterface",false)).FirstOrDefault(t=>t!=null);
   var method=type?.GetMethod("RegisterPage",BindingFlags.Public|BindingFlags.Static);
   if(method==null){logger.LogWarning("WUN web page requires Plugin Pages and File Transformation.");return Task.CompletedTask;}
   var json=JsonSerializer.Serialize(new{id="wun-requests",url="/WUN/web/requests.html",displayText="Requests",icon="room_service"});
   var value=method.GetParameters()[0].ParameterType.GetMethod("Parse",[typeof(string)])!.Invoke(null,[json]);method.Invoke(null,[value]);
  }catch(Exception error){logger.LogError(error,"WUN page registration failed.");}
  return Task.CompletedTask;
 }
 public Task StopAsync(CancellationToken ct)=>Task.CompletedTask;
}
