using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;

namespace Jellyfin.Plugin.Plm;

public sealed record MediaCard(Guid ItemId, string Status, string? Name, string? Kind, string[] Genres, bool Played = false, long PositionTicks = 0);
public interface ICatalogAccess
{
    IReadOnlyDictionary<Guid,Guid> Displays(Guid owner,IReadOnlyList<Guid> ids) => ids.Distinct().ToDictionary(id=>id,id=>id);
    MediaCard Resolve(Guid owner, Guid id);
    IReadOnlyDictionary<Guid,MediaCard> ResolveMany(Guid owner,IReadOnlyList<Guid> ids);
    CatalogPage Query(Guid owner,IReadOnlyList<Guid> ids,CatalogFilter filter,int start,int limit);
}
public sealed class CatalogAccess(ILibraryManager library, IUserManager users, IUserDataManager data) : ICatalogAccess
{
    public IReadOnlyDictionary<Guid,Guid> Displays(Guid owner,IReadOnlyList<Guid> ids)
    {
        var user=users.GetUserById(owner)??throw new UnauthorizedAccessException();
        var allowed=new Dictionary<Guid,BaseItem>();
        void Load(IEnumerable<Guid> keys) { foreach(var batch in keys.Distinct().Where(id=>!allowed.ContainsKey(id)).Chunk(200))
            foreach(var item in library.GetItemsResult(new InternalItemsQuery{User=user,ItemIds=batch,Recursive=true,IncludeAlternateVersions=true,EnableTotalRecordCount=false}).Items)
                allowed[item.Id]=item; }
        Load(ids);
        foreach(var id in ids.Distinct().Where(id=>!allowed.ContainsKey(id))) { var item=library.GetItemById(id);if(item is Video && item.IsVisible(user))allowed[id]=item; }
        for(var depth=0;depth<8;depth++) { var parents=allowed.Values.OfType<Video>().Where(v=>v.PrimaryVersionId.HasValue&&v.PrimaryVersionId.Value!=Guid.Empty&&!allowed.ContainsKey(v.PrimaryVersionId.Value)).Select(v=>v.PrimaryVersionId!.Value).Distinct().ToArray();if(parents.Length==0)break;var before=allowed.Count;Load(parents);foreach(var id in parents.Where(id=>!allowed.ContainsKey(id))) {var item=library.GetItemById(id);if(item is Video&&item.IsVisible(user))allowed[id]=item;}if(before==allowed.Count)break; }
        var result=new Dictionary<Guid,Guid>();
        foreach(var id in ids.Distinct()) {var seen=new HashSet<Guid>();var current=id;while(allowed.TryGetValue(current,out var item)&&seen.Add(current)) {
            if(item is Video video&&video.PrimaryVersionId is Guid parent&&parent!=Guid.Empty) {current=parent;continue;}
            if(item.IsVisibleStandalone(user))result[id]=current;break;
        }}
        return result;
    }
    public MediaCard Resolve(Guid owner, Guid id)
    {
        return ResolveMany(owner,[id]).GetValueOrDefault(id)??new(id,"unavailable",null,null,[]);
    }
    public IReadOnlyDictionary<Guid,MediaCard> ResolveMany(Guid owner,IReadOnlyList<Guid> ids)
    {
        var user = users.GetUserById(owner) ?? throw new UnauthorizedAccessException();
        if(ids.Count==0)return new Dictionary<Guid,MediaCard>();
        var items=library.GetItemsResult(new InternalItemsQuery{User=user,ItemIds=ids.ToArray(),Recursive=true,Limit=ids.Count,EnableTotalRecordCount=false}).Items;
        var result=new Dictionary<Guid,MediaCard>();
        foreach(var item in items){if(!item.IsVisibleStandalone(user)||!Supported.Contains(item.GetType().Name))continue;var progress=data.GetUserData(user,item);result[item.Id]=new(item.Id,"catalogued",item.Name,item.GetType().Name,item.Genres,progress?.Played??false,progress?.PlaybackPositionTicks??0);}
        return result;
    }
    private static readonly string[] Supported=["Movie","Series","Season","Episode","Audio","MusicAlbum","AudioBook","Book","Photo","PhotoAlbum","Video","MusicVideo"];
    public CatalogPage Query(Guid owner,IReadOnlyList<Guid> ids,CatalogFilter filter,int start,int limit)
    {
        var user=users.GetUserById(owner)??throw new UnauthorizedAccessException();if(ids.Count==0)return new(0,[]);
        var sort=filter.Sort switch{"year"=>ItemSortBy.ProductionYear,"created"=>ItemSortBy.DateCreated,"premiere"=>ItemSortBy.PremiereDate,_=>ItemSortBy.SortName};
        var query=new InternalItemsQuery{User=user,ItemIds=ids.ToArray(),Recursive=true,StartIndex=start,Limit=limit,EnableTotalRecordCount=true,Genres=filter.Genres,SearchTerm=filter.Search,IncludeItemTypes=(filter.Types.Length>0?filter.Types:Supported).Select(Enum.Parse<BaseItemKind>).ToArray(),IsPlayed=filter.Progress=="played"?true:filter.Progress=="unplayed"?false:null,IsResumable=filter.Progress=="resume"?true:null,IsFavorite=filter.Favorite,Years=filter.Year.HasValue?[filter.Year.Value]:[],OrderBy=[(sort,filter.Descending?SortOrder.Descending:SortOrder.Ascending)]};
        var page=library.GetItemsResult(query);var cards=new List<MediaCard>();
        foreach(var item in page.Items){if(!item.IsVisibleStandalone(user))continue;var progress=data.GetUserData(user,item);cards.Add(new(item.Id,"catalogued",item.Name,item.GetType().Name,item.Genres,progress?.Played??false,progress?.PlaybackPositionTicks??0));}
        return new(page.TotalRecordCount,cards);
    }
}
