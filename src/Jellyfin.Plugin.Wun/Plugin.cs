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
public sealed class Configuration:BasePluginConfiguration {}
public sealed class Plugin(IApplicationPaths paths,IXmlSerializer serializer):BasePlugin<Configuration>(paths,serializer)
{
 public override string Name=>"What U Need?";
 public override Guid Id=>Guid.Parse("1d91696c-0fdd-46da-8734-3a3d94e33002");
 public override string Description=>"Community catalog requests. Public beta for Jellyfin 12.4.";
}
public sealed class Registrator:IPluginServiceRegistrator
{
 public void RegisterServices(IServiceCollection services,IServerApplicationHost host)
 {
  services.AddSingleton<RequestStore>(sp=>new(Path.Combine(sp.GetRequiredService<IApplicationPaths>().DataPath,"wun","wun.db")));
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
