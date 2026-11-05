using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Plm;

[ApiController]
[Route("PLM/web")]
public sealed class WebController : ControllerBase
{
    [AllowAnonymous][HttpGet("settings")]
    public object Settings()=>new {playlistsDisabled=Plugin.Instance?.Configuration.DisableNativePlaylists==true};
    [AllowAnonymous][HttpGet("navigation.js")]
    public IActionResult Navigation()=>File(typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Plm.Web.navigation.js")!,"application/javascript");
    [AllowAnonymous][HttpGet("navigation-state.js")]
    public IActionResult NavigationState()=>Resource("navigation-state.js","application/javascript");
    [AllowAnonymous][HttpGet("runtime.js")]
    public IActionResult Runtime()=>Resource("runtime.js","application/javascript");
    [AllowAnonymous][HttpGet("i18n.js")]
    public IActionResult Internationalization()=>Resource("i18n.js","application/javascript");
    [AllowAnonymous][HttpGet("locales/{locale}.json")]
    public IActionResult Locale(string locale){var stream=typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Plm.Web.locales."+locale+".json");return stream is null?NotFound():File(stream,"application/json; charset=utf-8");}
    [AllowAnonymous][HttpGet("interactions.js")]
    public IActionResult Interactions()=>Resource("interactions.js","application/javascript");
    [AllowAnonymous][HttpGet("surface.js")]
    public IActionResult Surface()=>Resource("surface.js","application/javascript");
    [AllowAnonymous][HttpGet("providers.js")]
    public IActionResult Providers()=>Resource("providers.js","application/javascript");
    [AllowAnonymous][HttpGet("library.js")]
    public IActionResult Library()=>Resource("library.js","application/javascript");
    [AllowAnonymous][HttpGet("manage.js")]
    public IActionResult Manager()=>Resource("manage.js","application/javascript");
    [AllowAnonymous][HttpGet("style.css")]
    public IActionResult Style()=>Resource("style.css","text/css");
    [AllowAnonymous][HttpGet("manage.html")]
    public IActionResult Manage()=>Resource("manage.html","text/html; charset=utf-8");
    private IActionResult Resource(string name,string mime)=>File(typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Plm.Web."+name)!,mime);
    // Public template contains no account data. Every data route requires the Jellyfin session.
    [AllowAnonymous]
    [HttpGet("collections.html")]
    public IActionResult Page()
    {
        var stream = typeof(Plugin).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Plm.Web.collections.html");
        return stream is null ? NotFound() : File(stream, "text/html; charset=utf-8");
    }
}
