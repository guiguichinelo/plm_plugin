using Microsoft.AspNetCore.Mvc;
namespace Jellyfin.Plugin.Plm;

public sealed partial class CollectionsController
{
    // A read-only union: list membership never writes Jellyfin's native favorite state.
    [HttpGet("saved")]
    public IActionResult Saved([FromQuery]int startIndex=0,[FromQuery]int limit=40,[FromQuery]string? genres=null,[FromQuery]string? progress=null,[FromQuery]string? types=null,[FromQuery]string? search=null,[FromQuery]string? sort=null,[FromQuery]bool descending=false,[FromQuery]int? year=null,[FromQuery]bool? favorite=null,[FromQuery]Guid? libraryId=null,bool groupVersions=false)=>Handle(()=>
    {
        if(startIndex<0||limit is <1 or >200)throw new ArgumentException();
        var filter=CatalogFilter.Parse(genres,null,progress,types,search,sort??"name",descending,year,favorite,libraryId);
        catalog.ValidateScope(Owner,libraryId);
        var ids=store.List(Owner).SelectMany(c=>store.SelectionIds(Owner,c.Id)).Concat(catalog.FavoriteIds(Owner)).Distinct().ToArray();
        if(groupVersions)ids=Map(ids).Values.Distinct().ToArray();
        var page=catalog.Query(Owner,ids,filter,startIndex,limit);
        return new{page.TotalRecordCount,StartIndex=startIndex,Items=page.Items.Select(m=>new{ItemId=m.ItemId,DisplayItemId=m.ItemId,Media=m}).ToArray()};
    });
}
