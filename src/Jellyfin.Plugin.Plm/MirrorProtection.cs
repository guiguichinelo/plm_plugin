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
        // UserData updates cannot publish, delete or reassign a playlist. Still check
        // mirror ownership, but do not serialize unrelated users' progress writes.
        // All other native writes retain the creation/recovery mutation barrier.
        var userDataUpdate = nativeController == "UserData" && nativeAction == "UpdateItemUserData";
        var needsMutationGate = writing && !userDataUpdate;
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
