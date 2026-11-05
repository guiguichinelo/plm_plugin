namespace Plm.Core;

/// <summary>A remembered identity, never a permission grant or a filesystem reader.</summary>
public sealed record CatalogIdentity(Guid ItemId, string Kind, string Name, string? Path,
    int? Year, Dictionary<string, string> Providers, int? Season = null, int? Episode = null)
{
    public bool Matches(CatalogIdentity other)
    {
        if (Kind != other.Kind) return false;
        // Episode/season identity must be tied to its parent series, not an
        // ambiguous bare episode title or a provider's untyped numeric id.
        if (Kind is "Season" or "Episode" &&
            (Season != other.Season || Episode != other.Episode || Season is null)) return false;
        if (Kind=="Episode" && Episode is null) return false;
        var left=new Dictionary<string,string>(Providers,StringComparer.OrdinalIgnoreCase);
        var right=new Dictionary<string,string>(other.Providers,StringComparer.OrdinalIgnoreCase);
        var common = left.Keys.Intersect(right.Keys,StringComparer.OrdinalIgnoreCase).ToArray();
        return common.Length > 0 && common.All(k =>
            !string.IsNullOrWhiteSpace(left[k]) &&
            string.Equals(left[k], right[k], StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record RememberedMedia(Guid ReferenceId, Guid Owner, Guid CurrentItemId,
    CatalogIdentity Identity, bool? Favorite, long FavoriteRevision, bool PendingFavorite, bool Missing = false);

