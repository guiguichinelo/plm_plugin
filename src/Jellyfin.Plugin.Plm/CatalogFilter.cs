namespace Jellyfin.Plugin.Plm;
public sealed record CatalogFilter(string[] Genres,string Progress,string[] Types,string Search,string Sort,bool Descending,int? Year,bool? Favorite)
{
    public bool RequiresCatalog=>Genres.Length>0||Progress.Length>0||Types.Length>0||Search.Length>0||Sort!="custom"||Year.HasValue||Favorite.HasValue;
    public static CatalogFilter Parse(string? genres,string? genre,string? progress,string? types,string? search,string? sort,bool descending,int? year,bool? favorite)
    {
        var names=(genres??genre??"").Split('|',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var kinds=(types??"").Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Distinct().ToArray();
        var allowed=new[]{"Movie","Series","Season","Episode","Video","MusicVideo","Audio","MusicAlbum","AudioBook","Book","Photo","PhotoAlbum"};
        if(names.Length>30||names.Any(s=>s.Length>120)||kinds.Any(s=>!allowed.Contains(s))||(search?.Length??0)>120||progress is not (null or "" or "played" or "unplayed" or "resume")||sort is not (null or "custom" or "name" or "year" or "created" or "premiere")||year is <1 or >9999)throw new ArgumentException();
        return new(names,progress??"",kinds,(search??"").Trim(),sort??"custom",descending,year,favorite);
    }
}
public sealed record CatalogPage(int TotalRecordCount,IReadOnlyList<MediaCard> Items);
