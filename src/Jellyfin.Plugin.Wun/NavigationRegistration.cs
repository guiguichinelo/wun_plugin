using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Wun;
public sealed class NavigationPayload { public string? Contents {get;set;} }
public sealed class NavigationRegistration(ILogger<NavigationRegistration> log, ITaskManager tasks) : IHostedService
{
 public Task StartAsync(CancellationToken token)
 {
  tasks.TaskCompleted += OnTaskCompleted;
  try
  {
   var type=AssemblyLoadContext.All.SelectMany(c=>c.Assemblies).Select(a=>a.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface",false)).FirstOrDefault(t=>t is not null);
   var method=type?.GetMethod("RegisterTransformation",BindingFlags.Static|BindingFlags.Public);
   if(method is null)return Task.CompletedTask;
   var payload=JsonSerializer.Serialize(new{id="d71e7a93-5fe3-4cd3-b40c-6f22a79de003",fileNamePattern="index.html",callbackAssembly=typeof(Plugin).Assembly.FullName,callbackClass=typeof(NavigationRegistration).FullName,callbackMethod=nameof(TransformIndex)});
   var value=method.GetParameters()[0].ParameterType.GetMethod("Parse",[typeof(string)])!.Invoke(null,[payload]);method.Invoke(null,[value]);
  }
  catch(Exception error){log.LogError(error,"WUN navigation registration failed.");}
  return Task.CompletedTask;
 }
 public static string TransformIndex(NavigationPayload payload)
 {
  var html=payload.Contents??"";if(html.Contains("data-wun-navigation-script"))return html;
  return html.Replace("</body>","<script data-wun-navigation-script src=\"../WUN/web/header.js?v=0.1.0-beta.6\" defer></script></body>",StringComparison.OrdinalIgnoreCase);
 }
 private void OnTaskCompleted(object? sender, TaskCompletionEventArgs args)
 {
  if(args.Task.ScheduledTask.Key!="Jellyfin.Plugin.PluginPages.Startup")return;
  try{
   var type=AssemblyLoadContext.All.SelectMany(c=>c.Assemblies).Select(a=>a.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface",false)).FirstOrDefault(t=>t is not null);
   var method=type?.GetMethod("RegisterTransformation",BindingFlags.Static|BindingFlags.Public);if(method is null)return;
   foreach(var (file,registrationId) in new[]{("userpluginsettings.html","4da6413d-018a-4965-87d8-855a34853c34"),("user-plugin.undefined.chunk.js","4da6413d-018a-4965-87d8-855a34853c35"),("user-plugin-index-html.undefined.chunk.js","4da6413d-018a-4965-87d8-855a34853c36")}){
    var payload=JsonSerializer.Serialize(new{id=registrationId,fileNamePattern=file,callbackAssembly=typeof(Plugin).Assembly.FullName,callbackClass=typeof(PluginPageTemplate).FullName,callbackMethod=nameof(PluginPageTemplate.Transform)});
    method.Invoke(null,[method.GetParameters()[0].ParameterType.GetMethod("Parse",[typeof(string)])!.Invoke(null,[payload])]);
   }
  }catch(Exception error){log.LogError(error,"WUN plugin page loader registration failed.");}
 }
 public Task StopAsync(CancellationToken token){tasks.TaskCompleted-=OnTaskCompleted;return Task.CompletedTask;}
}
