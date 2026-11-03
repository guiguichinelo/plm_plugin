using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Plm;
public sealed class NavigationPayload { public string? Contents {get;set;} }
public sealed class NavigationRegistration(ILogger<NavigationRegistration> log) : IHostedService
{
 public Task StartAsync(CancellationToken token)
 {
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
  var html=payload.Contents??"";if(html.Contains("data-plm-navigation-script"))return html;
  return html.Replace("</body>","<script data-plm-runtime-script src=\"../PLM/web/runtime.js?v=ux7\" defer></script><script data-plm-i18n-script src=\"../PLM/web/i18n.js?v=ux7\" defer></script><script src=\"../PLM/web/interactions.js?v=ux7\" defer></script><script src=\"../PLM/web/surface.js?v=ux7\" defer></script><script src=\"../PLM/web/providers.js?v=ux7\" defer></script><script data-plm-navigation-script src=\"../PLM/web/navigation.js?v=ux7\" defer></script></body>",StringComparison.OrdinalIgnoreCase);
 }
 public Task StopAsync(CancellationToken token)=>Task.CompletedTask;
}
