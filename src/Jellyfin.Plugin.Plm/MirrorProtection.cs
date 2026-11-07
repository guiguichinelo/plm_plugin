using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Http;
using Plm.Core;

namespace Jellyfin.Plugin.Plm;

/// <summary>Protects PLM-owned playlist IDs on native routes, including bulk query endpoints.</summary>
public sealed class MirrorProtection(LibraryStore store, MirrorMutationGate gate) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        context.ActionDescriptor.RouteValues.TryGetValue("controller",out var nativeController);
        context.ActionDescriptor.RouteValues.TryGetValue("action",out var nativeAction);
        if(Plugin.Instance?.Configuration.DisableNativePlaylists==true&&nativeController=="Playlists")
        {context.Result=new ObjectResult(new{error="playlists_disabled",message="Use Minha coleção."}){StatusCode=403};return;}
        if (context.Controller is CollectionsController or AdminController or WebController) { await next(); return; }
        var references = new HashSet<Guid>();
        void Parse(string? text) { foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)) if (Guid.TryParse(part, out var id)) references.Add(id); }
        foreach (var value in context.RouteData.Values.Values) Parse(value?.ToString());
        foreach (var pair in context.HttpContext.Request.Query) foreach (var s in pair.Value) Parse(s);
        // Bound arguments include bulk DTOs that do not expose IDs as route/query values.
        foreach (var value in context.ActionArguments.Values)
        {
            if (value is Guid id) references.Add(id);
            else if (value is IEnumerable<Guid> ids) references.UnionWith(ids);
            else if (value is not null)
                foreach (var name in new[] { "Id", "Ids", "ItemId", "ItemIds", "PlaylistId" })
                {
                    var property = value.GetType().GetProperty(name);
                    if (property is null || property.GetIndexParameters().Length != 0) continue;
                    var nested = property.GetValue(value);
                    if (nested is Guid key) references.Add(key);
                    else if (nested is IEnumerable<Guid> keys) references.UnionWith(keys);
                    else if (nested is string text) Parse(text);
                }
        }
        // Other plugins legitimately use Guid.Empty as a "clear selection" sentinel.
        // It cannot identify a PLM mirror and must not be passed to the store's key validation.
        references.Remove(Guid.Empty);
        if (references.Count == 0) { await next(); return; }
        var writing = !HttpMethods.IsGet(context.HttpContext.Request.Method) && !HttpMethods.IsHead(context.HttpContext.Request.Method);
        // Only structural item/playlist writes can race mirror creation/recovery.
        // A slow image download, playback probe or metadata lookup must never
        // hold up unrelated commands such as stopping a scheduled task.
        // Existing mirror ownership is still checked below for every route.
        var userDataUpdate = nativeController == "UserData" && nativeAction == "UpdateItemUserData";
        var configuration = Plugin.Instance?.Configuration;
        var canCreateMirrors = configuration is null || (configuration.EnableMirrors && !configuration.DisableNativePlaylists);
        var structuralWrite = nativeController is "Items" or "ItemUpdate" or "Playlists";
        var needsMutationGate = writing && structuralWrite && canCreateMirrors;
        if (needsMutationGate) await gate.Semaphore.WaitAsync(context.HttpContext.RequestAborted);
        try
        {
            var affected = store.FindMirrors(references).ToArray();
            if (affected.Length > 0)
            {
                Guid.TryParse(context.HttpContext.User.FindFirst("Jellyfin-UserId")?.Value, out var owner);
                if (affected.Any(m => m.Owner != owner)) { context.Result = new NotFoundResult(); return; }
                var request = context.HttpContext.Request;
                context.ActionDescriptor.RouteValues.TryGetValue("controller", out var controller);
                var progress = userDataUpdate || controller is "Playstate" or "UserLibrary";
                if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !progress)
                { context.Result = new ConflictObjectResult(new { error = "plm_managed_playlist", message = "Manage this collection through PLM." }); return; }
            }
            await next();
        }
        finally { if (needsMutationGate) gate.Semaphore.Release(); }
    }
}
