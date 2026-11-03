using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.Plm;

public sealed class PageRegistration(ILogger<PageRegistration> log) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var type = AssemblyLoadContext.All.SelectMany(c => c.Assemblies).Select(a => a.GetType("Jellyfin.Plugin.PluginPages.PluginInterface", false)).FirstOrDefault(t => t is not null);
        if (type is null) { log.LogWarning("PLM web menu requires Plugin Pages and File Transformation matching the server version."); return Task.CompletedTask; }
        try
        {
            var method = type.GetMethod("RegisterPage", BindingFlags.Public | BindingFlags.Static)!;
            var payload = JsonSerializer.Serialize(new { id = "plm-collections", url = "/PLM/web/collections.html", displayText = "Favoritos", icon = "favorite" });
            var value = method.GetParameters()[0].ParameterType.GetMethod("Parse", [typeof(string)])!.Invoke(null, [payload]);
            method.Invoke(null, [value]);
        }
        catch (Exception error) { log.LogError(error, "Could not register the PLM web page."); }
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
