namespace Plm.Core;

public sealed record PersonalCollection(Guid Id, string Name, bool WatchLater, long Revision, DateTimeOffset? DeletedAt);
public sealed record Selection(Guid ItemId, int Position, DateTimeOffset AddedAt, DateTimeOffset? DeletedAt);
public sealed record CollectionDetails(PersonalCollection Collection, IReadOnlyList<Selection> Items);
public sealed record MaintenancePreview(int ExpiredCollections, int ExpiredSelections);
public sealed record ExportCollection(string Name, bool WatchLater, Guid[] Items);
public sealed record LibraryExport(int Version, ExportCollection[] Collections);
public sealed record Mirror(Guid CollectionId, Guid Owner, string MediaType, Guid PlaylistId, long Revision);
public sealed record MirrorSource(Guid Id, Guid Owner, long Revision, DateTimeOffset? DeletedAt);
public sealed record CollectionMembership(PersonalCollection Collection,bool Contains);
public sealed record SelectionPage(PersonalCollection Collection,int TotalRecordCount,IReadOnlyList<Selection> Items);
public sealed record ArtworkIndex(string Key,string Version);
public sealed record ListArtwork(byte[] Jpeg,string Version);
public sealed class CollectionMissingException : Exception;
public sealed class RevisionConflictException : Exception;
public sealed class InvalidSelectionException(string message) : Exception(message);

public static class MediaProjection
{
    // Domain layer never reads media paths or files. Adapters supply authorized leaves.
    public static IReadOnlyList<Guid> Deduplicate(IEnumerable<Guid> items) => items.Where(x => x != Guid.Empty).Distinct().ToArray();
}
