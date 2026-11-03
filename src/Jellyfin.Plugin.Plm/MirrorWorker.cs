using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Playlists;
using MediaBrowser.Model.Playlists;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plm.Core;

namespace Jellyfin.Plugin.Plm;

public sealed class MirrorWorker(LibraryStore store, ILibraryManager library, IUserManager users, IPlaylistManager playlists, ICatalogAccess catalog, MirrorMutationGate gate, ILogger<MirrorWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Explicit opt-in until the deployment's native-client privacy checks pass.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (var source in store.Sources())
                {
                    if (stoppingToken.IsCancellationRequested) break;
                    try { await Synchronize(source, stoppingToken); }
                    catch (Exception e) when (e is not OperationCanceledException) { log.LogWarning(e, "PLM mirror synchronization failed for collection {CollectionId}; retrying next cycle.", source.Id); }
                }
                foreach (var owner in store.Owners())
                {
                    var now = DateTimeOffset.UtcNow;
                    var missing = store.ObserveOwner(owner, users.GetUserById(owner) is not null, now);
                    if (missing < now.AddDays(-30)) store.PurgeMissingOwner(owner, now);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                log.LogError(error, "PLM maintenance cycle failed; retrying without stopping Jellyfin.");
            }
            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
    private async Task Synchronize(MirrorSource source, CancellationToken token)
    {
        var user = users.GetUserById(source.Owner); var now = DateTimeOffset.UtcNow;
        var missingSince = store.ObserveOwner(source.Owner, user is not null, now);
        if (source.DeletedAt < now.AddDays(-30) || missingSince < now.AddDays(-30))
        {
            foreach (var mirror in store.Mirrors(source.Id))
            {
                // Ledger + actual type + owner must agree. Never delete a media item.
                var entry = library.GetItemById(mirror.PlaylistId);
                if (entry is not null)
                {
                    if (entry is not Playlist owned || owned.OwnerUserId != source.Owner) throw new InvalidOperationException("Mirror ownership mismatch during cleanup.");
                    library.DeleteItem(owned, new DeleteOptions { DeleteFileLocation = false, DeleteFromExternalProvider = false });
                }
                store.ForgetMirror(mirror.PlaylistId);
            }
            return;
        }
        if (user is null) return; // Deleted accounts are never reassigned by username.
        if (Plugin.Instance?.Configuration.EnableMirrors != true||Plugin.Instance.Configuration.DisableNativePlaylists) return;
        var details = store.Get(source.Owner, source.Id, true);
        var leaves = new List<BaseItem>();
        if (source.DeletedAt is null)
            foreach (var selection in details.Items.Where(i => i.DeletedAt is null))
            {
                token.ThrowIfCancellationRequested(); if (catalog.Resolve(source.Owner, selection.ItemId).Status != "catalogued") continue;
                var item = library.GetItemsResult(new InternalItemsQuery { User = user, ItemIds = [selection.ItemId], Recursive = true, Limit = 1 }).Items.FirstOrDefault();
                if (item is null) continue;
                if (item.IsFolder)
                {
                    // Explicit user scoping on every descendant query, bounded pages.
                    for (var offset = 0; ; offset += 200)
                    {
                        var children = library.GetItemsResult(new InternalItemsQuery { User = user, ParentId = item.Id, Recursive = true, StartIndex = offset, Limit = 200, IsFolder = false, OrderBy = [(ItemSortBy.ParentIndexNumber, SortOrder.Ascending), (ItemSortBy.IndexNumber, SortOrder.Ascending), (ItemSortBy.SortName, SortOrder.Ascending)] }).Items;
                        leaves.AddRange(children.Where(c => c.IsVisibleStandalone(user) && c.SupportsAddingToPlaylist)); if (children.Count < 200) break;
                        token.ThrowIfCancellationRequested();
                    }
                }
                else if (item.SupportsAddingToPlaylist) leaves.Add(item);
            }
        var groups = leaves.DistinctBy(i => i.Id).GroupBy(i => i.MediaType).Where(g => g.Key is not MediaType.Unknown).ToDictionary(g => g.Key, g => g.Select(i => i.Id).ToArray());
        var mirrors = store.Mirrors(source.Id);
        foreach (var kind in groups.Keys.Concat(mirrors.Select(m => Enum.Parse<MediaType>(m.MediaType))).Distinct())
        {
            var mirror = mirrors.FirstOrDefault(m => m.MediaType == kind.ToString()); var ids = groups.GetValueOrDefault(kind) ?? [];
            var playlist = mirror is null ? null : playlists.GetPlaylistForUser(mirror.PlaylistId, source.Owner);
            if (playlist is null && ids.Length == 0) continue;
            var family=kind.ToString() switch {"Video"=>"Vídeos","Audio"=>"Áudio","Photo"=>"Fotos","Book"=>"Livros",var other=>other};
            var name = $"{details.Collection.Name} (PLM · {family})";
            var pendingName=$"PLM pending {source.Id:N} {kind}";
            if (playlist is null)
            {
                await gate.Semaphore.WaitAsync(token);
                try
                {
                    // Recover a crash between host creation and ledger persistence, without adopting ordinary playlists.
                    playlist = playlists.GetPlaylists(source.Owner).FirstOrDefault(p => p.Name == pendingName && p.OwnerUserId == source.Owner && !p.OpenAccess && p.Shares.Count == 0 && !p.GetManageableItems().Any());
                    Guid id;
                    if (playlist is null) { var created = await playlists.CreatePlaylist(new PlaylistCreationRequest { Name = pendingName, UserId = source.Owner, MediaType = kind, Public = false, Users = [], ItemIdList = [] }); id = Guid.Parse(created.Id); }
                    else id = playlist.Id;
                    store.SaveMirror(source.Id, kind.ToString(), id, -1); playlist = playlists.GetPlaylistForUser(id, source.Owner);
                }
                finally { gate.Semaphore.Release(); }
            }
            if (playlist.OwnerUserId != source.Owner) throw new InvalidOperationException("Mirror owner mismatch.");
            var current = playlist.GetManageableItems().Select(i => i.Item2.Id).ToArray();
            if (!current.SequenceEqual(ids) || playlist.OpenAccess || playlist.Shares.Count != 0 || playlist.Name != name)
                await playlists.UpdatePlaylist(new PlaylistUpdateRequest { Id = playlist.Id, UserId = source.Owner, Name = name, Public = false, Users = [], Ids = ids });
            store.SaveMirror(source.Id, kind.ToString(), playlist.Id, source.Revision);
        }
        store.CompleteJob(source.Id, source.Revision);
    }
}
