using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Plm;

// Plugin Pages 3.x selects the globally visible page after 100ms. With native
// route animations that can still be the outgoing page. Capture the template
// root synchronously, and never append another page's response into that root.
public static class PluginPageTemplate
{
    public static string Transform(NavigationPayload payload)
    {
        var html = payload.Contents ?? "";
        if (html.Contains("pluginPagesOwnedLoader")) return html;
        var pattern = @"setTimeout\(function\(\)\{[\s\S]*?\},\s*100\);";
        if (!Regex.IsMatch(html, pattern)) return html;
        const string loader = """
        (function pluginPagesOwnedLoader(){
            const page = document.currentScript?.closest('[data-role="page"]');
            // Legacy's script evaluator does not always set currentScript.
            // Previously initialized cached pages have a marker; the new template
            // is the sole uninitialized container, even while still hidden.
            const container = page?.querySelector('.userPluginSettingsContainer') ||
                [...document.querySelectorAll('.userPreferencesPage .userPluginSettingsContainer:not([data-plugin-pages-owned])')]
                    .find(node=>node.parentElement.querySelector('script')?.textContent.includes('pluginPagesOwnedLoader'));
            const owner = ApiClient.getCurrentUserId();
            const path = new URL(location.hash.slice(1), location.origin).searchParams.get('pageUrl');
            if (!container || !path) return;
            container.dataset.pluginPagesOwned = path;
            const url = path.startsWith('/') ? ApiClient.getUrl(path) : path;
            ApiClient.ajax({type:'GET',url}).then(response=>{
                if (!container.isConnected || ApiClient.getCurrentUserId()!==owner) return;
                container.replaceChildren(document.createRange().createContextualFragment(response));
            }).catch(()=>{if(container.isConnected)container.textContent='Could not load this plugin page.';});
        })();
        """;
        // Plugin Pages also embeds this template in two virtual webpack chunks.
        var replacement = html.Contains("e.default = '")
            ? loader.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("'", "\\'")
            : loader;
        return Regex.Replace(html, pattern, _ => replacement, RegexOptions.None);
    }
}
