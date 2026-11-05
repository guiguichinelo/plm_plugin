using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plm.Core;
using System.Text.Json;

namespace Jellyfin.Plugin.Plm;

public sealed record CreateRequest(string Name);
public sealed record EditRequest(long Revision, string? Name = null, Guid? ItemId = null, Guid[]? Order = null);
public sealed record MoveRequest(long Revision, Guid ItemId, int Offset);
public sealed record WatchRequest([property:System.Text.Json.Serialization.JsonRequired]bool Saved);

[ApiController]
[Authorize]
[Route("PLM/v1/me")]
public sealed class CollectionsController(LibraryStore store, ICatalogAccess catalog) : ControllerBase
{
    private Guid Owner => Guid.TryParse(User.FindFirst("Jellyfin-UserId")?.Value, out var id) && id != Guid.Empty ? id : throw new UnauthorizedAccessException();
    private IActionResult Handle(Func<object?> work)
    {
        Response.Headers.CacheControl="private, no-store";
        try { return Ok(work()); }
        catch (CollectionMissingException) { return NotFound(); }
        catch (RevisionConflictException) { return Conflict(new { error = "revision_conflict" }); }
        catch (UnauthorizedAccessException) { return Unauthorized(); }
        catch (ArgumentException) { return BadRequest(new { error = "invalid_request" }); }
        catch (JsonException) { return BadRequest(new { error = "invalid_json" }); }
        catch (InvalidSelectionException) { return BadRequest(new { error = "item_unavailable" }); }
    }
    [HttpGet("collections")]
    public IActionResult List([FromQuery] bool trash = false) => Handle(() => store.List(Owner, trash));
    [HttpGet("items/memberships")]
    public IActionResult SavedMembership([FromQuery]string ids)=>Handle(()=>{
        var parts=ids.Split(',',StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length>200||parts.Any(p=>!Guid.TryParse(p,out var id)||id==Guid.Empty))throw new ArgumentException();
        var visible=catalog.ResolveMany(Owner,parts.Select(Guid.Parse).Distinct().ToArray()).Where(x=>x.Value.Status=="catalogued").Select(x=>x.Key).ToArray();
        return store.SavedMemberships(Owner,visible);
    });
    [HttpPost("collections")]
    public IActionResult Create(CreateRequest body) => Handle(() => store.Create(Owner, body.Name));
    [HttpPost("watch-later")]
    public IActionResult WatchLater() => Handle(() => store.Create(Owner, "Assistir mais tarde", true));
    [HttpGet("watch-later/membership")]
    public IActionResult WatchMembership([FromQuery]string ids)=>Handle(()=>{var parts=ids.Split(',',StringSplitOptions.RemoveEmptyEntries);if(parts.Length>200||parts.Any(p=>!Guid.TryParse(p,out var id)||id==Guid.Empty))throw new ArgumentException();return store.WatchMemberships(Owner,parts.Select(Guid.Parse).Distinct().ToArray());});
    [HttpPut("watch-later/items/{id:guid}")]
    public IActionResult SetWatch(Guid id,WatchRequest body)=>Handle(()=>{if(body.Saved&&catalog.Resolve(Owner,id).Status!="catalogued")throw new InvalidSelectionException("Unavailable item.");store.SetWatchMembership(Owner,id,body.Saved);return new{saved=body.Saved};});
    [HttpGet("items/{itemId:guid}/memberships")]
    public IActionResult Memberships(Guid itemId)=>Handle(()=>
    {
        if(catalog.Resolve(Owner,itemId).Status!="catalogued")throw new InvalidSelectionException("Unavailable item.");
        store.Create(Owner,"Assistir mais tarde",true);return store.Memberships(Owner,itemId);
    });
    [HttpGet("collections/{id:guid}")]
    public IActionResult Get(Guid id, [FromQuery] bool trash = false, [FromQuery] int startIndex = 0, [FromQuery] int limit = 40, [FromQuery] string? genre = null, [FromQuery] string? progress = null,[FromQuery]string? genres=null,[FromQuery]string? types=null,[FromQuery]string? search=null,[FromQuery]string? sort=null,[FromQuery]bool descending=false,[FromQuery]int? year=null,[FromQuery]bool? favorite=null) => Handle(() =>
    {
        if(startIndex<0||limit is <1 or >200)throw new ArgumentException();var filter=CatalogFilter.Parse(genres,genre,progress,types,search,sort,descending,year,favorite);
        if(!filter.RequiresCatalog){var page=store.Page(Owner,id,startIndex,limit,trash);var media=catalog.ResolveMany(Owner,page.Items.Select(e=>e.ItemId).ToArray());return new{page.Collection,page.TotalRecordCount,StartIndex=startIndex,Items=page.Items.Select(e=>new{e.ItemId,e.Position,e.AddedAt,e.DeletedAt,Media=media.GetValueOrDefault(e.ItemId)??new MediaCard(e.ItemId,"unavailable",null,null,[])}).ToArray()};}
        var collection=store.Page(Owner,id,0,1,trash).Collection;var ids=store.SelectionIds(Owner,id,trash);var matched=catalog.Query(Owner,ids,filter,startIndex,limit);var selection=store.SelectionDetails(Owner,id,matched.Items.Select(m=>m.ItemId).ToArray());
        return new{Collection=collection,matched.TotalRecordCount,StartIndex=startIndex,Items=matched.Items.Where(m=>selection.ContainsKey(m.ItemId)).Select(m=>new{m.ItemId,selection[m.ItemId].Position,selection[m.ItemId].AddedAt,selection[m.ItemId].DeletedAt,Media=m}).ToArray()};
    });
    [HttpPut("collections/{id:guid}")]
    public IActionResult Rename(Guid id, EditRequest body) => Handle(() => { store.Rename(Owner, id, body.Name ?? "", body.Revision); return null; });
    [HttpPost("collections/{id:guid}/items")]
    public IActionResult Add(Guid id, EditRequest body) => Handle(() =>
    {
        var item = body.ItemId ?? Guid.Empty;
        store.Get(Owner, id);
        if (item == Guid.Empty || catalog.Resolve(Owner, item).Status != "catalogued") throw new InvalidSelectionException("Unavailable item.");
        store.Add(Owner, id, item, body.Revision);
        return new { added = true };
    });
    [HttpDelete("collections/{id:guid}/items/{item:guid}")]
    public IActionResult Remove(Guid id, Guid item, [FromQuery] long revision) => Handle(() => { store.Remove(Owner, id, item, revision); return null; });
    [HttpPost("collections/{id:guid}/items/{item:guid}/restore")]
    public IActionResult RestoreItem(Guid id, Guid item, EditRequest body) => Handle(() => { store.RestoreSelection(Owner, id, item, body.Revision); return null; });
    [HttpPut("collections/{id:guid}/order")]
    public IActionResult Reorder(Guid id, EditRequest body) => Handle(() => { store.Reorder(Owner, id, body.Order ?? [], body.Revision); return null; });
    [HttpPost("collections/{id:guid}/move")]
    public IActionResult Move(Guid id, MoveRequest body) => Handle(() =>
    {
        if (body.Offset is not (-1 or 1)) throw new ArgumentException();
        var order = store.Get(Owner, id).Items.Select(i => i.ItemId).ToArray(); var index = Array.IndexOf(order, body.ItemId);
        if (index < 0 || index + body.Offset < 0 || index + body.Offset >= order.Length) throw new ArgumentException();
        (order[index], order[index + body.Offset]) = (order[index + body.Offset], order[index]); store.Reorder(Owner, id, order, body.Revision); return null;
    });
    [HttpDelete("collections/{id:guid}")]
    public IActionResult Delete(Guid id, [FromQuery] long revision) => Handle(() => { store.SetDeleted(Owner, id, revision, true); return null; });
    [HttpPost("collections/{id:guid}/restore")]
    public IActionResult Restore(Guid id, EditRequest body) => Handle(() => { store.SetDeleted(Owner, id, body.Revision, false); return null; });
    [HttpGet("preferences")]
    public IActionResult Preferences() => Handle(() => JsonSerializer.Deserialize<JsonElement>(store.GetPreferences(Owner)));
    [HttpPut("preferences")]
    public IActionResult SetPreferences(JsonElement body) => Handle(() => { store.SetPreferences(Owner, body.GetRawText()); return null; });
    [HttpGet("export")]
    public IActionResult Export() => Handle(() => store.Export(Owner));
    [HttpPost("import")]
    [RequestSizeLimit(2097152)]
    public IActionResult Import(LibraryExport body) => Handle(() => store.Import(Owner, body));
}

[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("PLM/v1/admin")]
public sealed class AdminController(LibraryStore store) : ControllerBase
{
    [HttpGet("health")]
    public object Health() => new { plugin = "PLM", version = "0.1.0-beta.3", target = "12.4.0", stage = "beta", mirrorsEnabled = Plugin.Instance?.Configuration.EnableMirrors == true, userInterfaceReady = true };
    [HttpGet("maintenance/preview")]
    public object Preview() => store.PreviewCleanup(DateTimeOffset.UtcNow);
    [HttpPost("maintenance/execute")]
    public object Cleanup() => store.Cleanup(DateTimeOffset.UtcNow);
    [HttpGet("backup")]
    public IActionResult Backup()
    {
        var path = Path.Combine(Path.GetTempPath(), "plm-backup-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            store.Backup(path);
            if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return File(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose), "application/vnd.sqlite3", "plm-backup.db");
        }
        catch { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); throw; }
    }
}
