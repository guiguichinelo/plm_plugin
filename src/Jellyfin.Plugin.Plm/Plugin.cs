using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Plm.Core;

namespace Jellyfin.Plugin.Plm;

public sealed class Configuration : BasePluginConfiguration { public bool EnableMirrors { get; set; } public bool DisableNativePlaylists { get; set; } }
public sealed class Plugin : BasePlugin<Configuration>, IHasWebPages
{
    public static Plugin? Instance { get; private set; }
    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) { Instance = this; }
    public override string Name => "Personal Library Manager";
    public override Guid Id => Guid.Parse("9963ad46-7894-424f-b854-786f370a81a3");
    public override string Description => "Private personal collections and Watch Later. Beta 5 for Jellyfin 12.4.0; Linux x64.";
    public IEnumerable<PluginPageInfo> GetPages() => [new() { Name = "plm", EmbeddedResourcePath = "Jellyfin.Plugin.Plm.Web.admin.html" }];
}
public sealed class Registrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost host)
    {
        services.AddSingleton<LibraryStore>(sp => new(Path.Combine(sp.GetRequiredService<IApplicationPaths>().DataPath, "plm", "plm.db")));
        services.AddSingleton<CatalogResilienceStore>(sp => {
            _ = sp.GetRequiredService<LibraryStore>();
            return new(Path.Combine(sp.GetRequiredService<IApplicationPaths>().DataPath, "plm", "plm.db"));
        });
        services.AddSingleton<CatalogResilience>();
        services.AddHostedService(sp => sp.GetRequiredService<CatalogResilience>());
        services.AddSingleton<ICatalogAccess, CatalogAccess>();
        services.AddHostedService<PageRegistration>();
        services.AddHostedService<NavigationRegistration>();
        services.AddHostedService<MirrorWorker>();
        services.AddSingleton<MirrorProtection>();
        services.AddSingleton<MirrorMutationGate>();
        services.AddSingleton<NativeFavoriteIntent>();
        services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o => {
            o.Filters.AddService<MirrorProtection>(int.MinValue);
            o.Filters.AddService<NativeFavoriteIntent>(int.MinValue+1);
        });
    }
}
