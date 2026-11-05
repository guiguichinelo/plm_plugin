using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Plm;
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
   var payload=JsonSerializer.Serialize(new{id="a0769481-9326-47ad-ae6c-13cef03296c9",fileNamePattern="index.html",callbackAssembly=typeof(Plugin).Assembly.FullName,callbackClass=typeof(NavigationRegistration).FullName,callbackMethod=nameof(TransformIndex)});
   var value=method.GetParameters()[0].ParameterType.GetMethod("Parse",[typeof(string)])!.Invoke(null,[payload]);method.Invoke(null,[value]);
  }
  catch(Exception error){log.LogError(error,"PLM navigation registration failed.");}
  return Task.CompletedTask;
 }
 public static string TransformIndex(NavigationPayload payload)
 {
  var html=payload.Contents??"";
  if(!html.Contains("data-plm-navigation-state-script"))html=html.Replace("</body>","<script data-plm-navigation-state-script src=\"../PLM/web/navigation-state.js?v=beta3\" defer></script></body>",StringComparison.OrdinalIgnoreCase);
  if(html.Contains("data-plm-navigation-script"))return html;
  return html.Replace("</body>","<script data-plm-runtime-script src=\"../PLM/web/runtime.js?v=beta3\" defer></script><script data-plm-i18n-script src=\"../PLM/web/i18n.js?v=beta3\" defer></script><script src=\"../PLM/web/interactions.js?v=beta3\" defer></script><script src=\"../PLM/web/surface.js?v=beta3\" defer></script><script src=\"../PLM/web/providers.js?v=beta3\" defer></script><script data-plm-navigation-script src=\"../PLM/web/navigation.js?v=beta3\" defer></script></body>",StringComparison.OrdinalIgnoreCase);
 }
 private void OnTaskCompleted(object? sender, TaskCompletionEventArgs args)
 {
  if(args.Task.ScheduledTask.Key!="Jellyfin.Plugin.PluginPages.Startup")return;
  try{
   var type=AssemblyLoadContext.All.SelectMany(c=>c.Assemblies).Select(a=>a.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface",false)).FirstOrDefault(t=>t is not null);
   var method=type?.GetMethod("RegisterTransformation",BindingFlags.Static|BindingFlags.Public);if(method is null)return;
   foreach(var (file,registrationId) in new[]{("userpluginsettings.html","7fbd5dd1-98e0-42da-a774-9bd4e12dbd03"),("user-plugin.undefined.chunk.js","7fbd5dd1-98e0-42da-a774-9bd4e12dbd05"),("user-plugin-index-html.undefined.chunk.js","7fbd5dd1-98e0-42da-a774-9bd4e12dbd06")}){
    var payload=JsonSerializer.Serialize(new{id=registrationId,fileNamePattern=file,callbackAssembly=typeof(Plugin).Assembly.FullName,callbackClass=typeof(PluginPageTemplate).FullName,callbackMethod=nameof(PluginPageTemplate.Transform)});
    method.Invoke(null,[method.GetParameters()[0].ParameterType.GetMethod("Parse",[typeof(string)])!.Invoke(null,[payload])]);
   }
  }catch(Exception error){log.LogError(error,"PLM plugin page loader registration failed.");}
 }
 public Task StopAsync(CancellationToken token){tasks.TaskCompleted-=OnTaskCompleted;return Task.CompletedTask;}
}
