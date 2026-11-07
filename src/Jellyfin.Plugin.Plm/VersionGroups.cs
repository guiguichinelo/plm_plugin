using Microsoft.AspNetCore.Mvc;
using Plm.Core;
namespace Jellyfin.Plugin.Plm;
public sealed record GroupChange(long Revision,Guid DisplayItemId,Guid[] SelectionIds);
public sealed partial class CollectionsController
{
    private Dictionary<Guid,Guid> Map(IEnumerable<Guid> ids)=>catalog.Displays(Owner,ids.Distinct().ToArray()).ToDictionary(x=>x.Key,x=>x.Value);
    private Guid Target(Guid id)=>Map([id]).GetValueOrDefault(id,Guid.Empty);
    private object GroupedPage(Guid id,bool trash,int start,int limit,CatalogFilter filter)
    {
        var list=store.Get(Owner,id,trash);var map=Map(list.Items.Select(x=>x.ItemId));
        var groups=list.Items.Where(x=>!trash||x.DeletedAt.HasValue).GroupBy(x=>map.GetValueOrDefault(x.ItemId,x.ItemId)).OrderBy(g=>g.Min(x=>x.Position)).ThenBy(g=>g.Key).ToArray();
        var count=groups.Length;IGrouping<Guid,Selection>[] selected;IReadOnlyDictionary<Guid,MediaCard> cards;
        if(filter.RequiresCatalog&&filter.Sort!="custom") {
            var page=catalog.Query(Owner,groups.Select(g=>g.Key).ToArray(),filter,start,limit);var lookup=groups.ToDictionary(g=>g.Key);
            selected=page.Items.Where(m=>lookup.ContainsKey(m.ItemId)).Select(m=>lookup[m.ItemId]).ToArray();count=page.TotalRecordCount;cards=page.Items.ToDictionary(m=>m.ItemId);
        } else {
            IEnumerable<IGrouping<Guid,Selection>> view=groups;
            if(filter.RequiresCatalog){var matching=catalog.Query(Owner,groups.Select(g=>g.Key).ToArray(),filter,0,Math.Max(1,groups.Length)).Items.Select(m=>m.ItemId).ToHashSet();view=view.Where(g=>matching.Contains(g.Key));}
            var filtered=view.ToArray();count=filtered.Length;selected=filtered.Skip(start).Take(limit).ToArray();cards=catalog.ResolveMany(Owner,selected.Select(g=>g.Key).ToArray());
        }
        var remembered=selected.Any(g=>!cards.ContainsKey(g.Key))?memory?.References(Owner).ToDictionary(x=>x.CurrentItemId):null;
        return new{list.Collection,TotalRecordCount=count,StartIndex=start,Items=selected.Select(g=>new{ItemId=g.First().ItemId,DisplayItemId=g.Key,SelectionIds=g.Select(x=>x.ItemId).ToArray(),Position=g.Min(x=>x.Position),AddedAt=g.Min(x=>x.AddedAt),DeletedAt=g.First().DeletedAt,Media=cards.GetValueOrDefault(g.Key)??new MediaCard(g.Key,"unavailable",remembered?.GetValueOrDefault(g.First().ItemId)?.Identity.Name,null,[])})};
    }
    private object GroupMemberships(Guid item)
    {
        var target=Target(item);if(target==Guid.Empty)throw new InvalidSelectionException("Unavailable item.");
        store.Create(Owner,"Assistir mais tarde",true);
        var lists=store.List(Owner);var selections=lists.ToDictionary(c=>c.Id,c=>store.SelectionIds(Owner,c.Id));var map=Map(selections.Values.SelectMany(x=>x));
        return lists.Select(c=>{var ids=selections[c.Id];var matches=ids.Where(id=>map.GetValueOrDefault(id)==target).ToArray();return new{Collection=c,Contains=matches.Length>0,DisplayItemId=target,SelectionIds=matches};}).ToArray();
    }
    private IReadOnlyList<Guid> GroupSaved(Guid[] requested,bool watch)
    {
        var selections=store.List(Owner).Where(c=>!watch||c.WatchLater).SelectMany(c=>store.SelectionIds(Owner,c.Id)).Distinct().ToArray();var map=Map(selections.Concat(requested));var saved=selections.Where(map.ContainsKey).Select(id=>map[id]).ToHashSet();return requested.Where(id=>map.TryGetValue(id,out var target)&&saved.Contains(target)).Distinct().ToArray();
    }
    private object GroupAdd(Guid id,EditRequest body)
    {
        var list=store.Get(Owner,id);if(list.Collection.Revision!=body.Revision)throw new RevisionConflictException();var item=body.ItemId??Guid.Empty;var target=Target(item);if(target==Guid.Empty)throw new InvalidSelectionException("Unavailable item.");
        if(!Map(list.Items.Select(x=>x.ItemId)).Values.Contains(target)){resilience?.Capture(Owner,item);store.Add(Owner,id,item,body.Revision);}
        return new{added=true,Revision=store.Get(Owner,id).Collection.Revision};
    }
    private object GroupWatch(Guid item,bool saved)
    {
        var c=store.Create(Owner,"Assistir mais tarde",true);if(saved)return GroupAdd(c.Id,new(c.Revision,ItemId:item));
        var map=Map(store.SelectionIds(Owner,c.Id).Append(item));if(!map.TryGetValue(item,out var target)){store.SetWatchMembership(Owner,item,false);return new{saved=false};}
        var ids=store.SelectionIds(Owner,c.Id).Where(id=>map.GetValueOrDefault(id)==target).ToArray();if(ids.Length>0)store.ChangeGroup(Owner,c.Id,ids,c.Revision,false);return new{saved=false};
    }
    [HttpPost("collections/{id:guid}/version-groups/remove")]
    public IActionResult RemoveGroup(Guid id,GroupChange body)=>ChangeVersionGroup(id,body,false);
    [HttpPost("collections/{id:guid}/version-groups/restore")]
    public IActionResult RestoreGroup(Guid id,GroupChange body)=>ChangeVersionGroup(id,body,true);
    private IActionResult ChangeVersionGroup(Guid id,GroupChange body,bool restore)=>Handle(()=>{
        var list=store.Get(Owner,id);if(body.SelectionIds is null||body.SelectionIds.Length==0)throw new ArgumentException();
        var candidates=(restore?store.Get(Owner,id,true):list).Items.Where(x=>restore?x.DeletedAt.HasValue:!x.DeletedAt.HasValue).ToArray();var map=Map(candidates.Select(x=>x.ItemId));
        var actual=candidates.Where(x=>map.GetValueOrDefault(x.ItemId,x.ItemId)==body.DisplayItemId).Select(x=>x.ItemId).ToHashSet();
        if(!actual.SetEquals(body.SelectionIds)||body.SelectionIds.Distinct().Count()!=body.SelectionIds.Length)throw new RevisionConflictException();
        return new{Revision=store.ChangeGroup(Owner,id,body.SelectionIds,body.Revision,restore)};
    });
}
