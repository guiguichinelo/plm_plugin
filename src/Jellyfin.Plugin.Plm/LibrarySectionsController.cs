using Microsoft.AspNetCore.Mvc;
namespace Jellyfin.Plugin.Plm;

public sealed partial class CollectionsController
{
    private IReadOnlyList<Guid> SectionIds(string list)=>list switch
    {
        "liked"=>catalog.FavoriteIds(Owner),
        "saved"=>store.List(Owner).SelectMany(c=>store.SelectionIds(Owner,c.Id)).Concat(catalog.FavoriteIds(Owner)).Distinct().ToArray(),
        _ when Guid.TryParse(list,out var id)&&id!=Guid.Empty=>store.SelectionIds(Owner,id),
        _=>throw new ArgumentException()
    };
    [HttpGet("facets")]
    public IActionResult Facets(string list="saved",Guid? libraryId=null)=>Handle(()=>
    {
        catalog.ValidateScope(Owner,libraryId);
        var ids=Map(SectionIds(list)).Values.Distinct().ToArray();
        var filter=CatalogFilter.Parse(null,null,null,null,null,"name",false,null,null,libraryId);
        var page=catalog.Query(Owner,ids,filter,0,Math.Max(1,ids.Length));
        return new{Genres=page.Items.SelectMany(i=>i.Genres).Where(g=>!string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray()};
    });
    [HttpGet("library-sections")]
    public IActionResult LibrarySections(Guid libraryId,int startIndex=0,int limit=6,int rowLimit=10,string? genres=null,string? progress=null,string? types=null,string? search=null,string? sort=null,bool descending=false,int? year=null,bool? favorite=null)=>Handle(()=>
    {
        if(startIndex<0||limit is <1 or >12||rowLimit is <1 or >20)throw new ArgumentException();
        var filter=CatalogFilter.Parse(genres,null,progress,types,search,sort??"name",descending,year,favorite,libraryId);catalog.ValidateScope(Owner,libraryId);
        var sources=new List<(string Id,string? Name,bool Liked,bool WatchLater,IReadOnlyList<Guid> Ids)>{("liked",null,true,false,catalog.FavoriteIds(Owner))};
        sources.AddRange(store.List(Owner).Select(c=>(c.Id.ToString("N"),(string?)c.Name,false,c.WatchLater,store.SelectionIds(Owner,c.Id))));
        var map=Map(sources.SelectMany(s=>s.Ids));
        var matched=catalog.MatchingIds(Owner,map.Values.Distinct().ToArray(),filter);
        var rows=sources.Select(s=>
        {
            var selected=s.Ids.Where(map.ContainsKey).Select(id=>map[id]).ToHashSet();
            return new{s.Id,s.Name,s.Liked,s.WatchLater,Items=matched.Where(selected.Contains).Distinct().ToArray()};
        }).Where(s=>s.Items.Length>0).ToArray();
        return new{TotalRecordCount=rows.Length,TotalItems=matched.Count,StartIndex=startIndex,HasMore=startIndex+limit<rows.Length,
            Sections=rows.Skip(startIndex).Take(limit).Select(s=>new{s.Id,s.Name,s.Liked,s.WatchLater,TotalRecordCount=s.Items.Length,ItemIds=s.Items.Take(rowLimit).ToArray()}).ToArray()};
    });
}
