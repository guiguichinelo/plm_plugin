using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Plm.Core;

namespace Jellyfin.Plugin.Plm;

/// <summary>Uses Jellyfin services, never the host database or media filesystem.</summary>
public sealed class CatalogResilience(CatalogResilienceStore memory, LibraryStore lists,
    ILibraryManager library, IUserManager users, IUserDataManager data,
    ILogger<CatalogResilience> log, FavoriteMutationGate favorites) : BackgroundService
{
    // Never hold a plugin monitor while calling host library/UserData services:
    // native save notifications can re-enter on a different thread.
    private long _quietSinceTicks = DateTimeOffset.UtcNow.UtcTicks;
    private DateTimeOffset QuietSince => new(Interlocked.Read(ref _quietSinceTicks), TimeSpan.Zero);
    private void ResetQuietPeriod() => Interlocked.Exchange(ref _quietSinceTicks, DateTimeOffset.UtcNow.UtcTicks);
    public bool BaselineComplete { get; private set; }
    public DateTimeOffset? LastSuccessfulCycle { get; private set; }
    public string? LastError { get; private set; }
    private static readonly string[] Supported = ["Movie", "Series", "Season", "Episode",
        "Video", "MusicVideo", "Audio", "MusicAlbum", "AudioBook", "Book"];

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        data.UserDataSaved += OnUserDataSaved;
        library.ItemRemoved += OnItemRemoved;
        return base.StartAsync(cancellationToken);
    }
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        data.UserDataSaved -= OnUserDataSaved;
        library.ItemRemoved -= OnItemRemoved;
        await base.StopAsync(cancellationToken);
    }
    private CatalogIdentity Identity(BaseItem item)
    {
        var providers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Only known, namespaced providers. Parent ids cannot be compared with
        // episode ids, even if their bare numeric values happen to coincide.
        if (item.GetType().Name is "Season" or "Episode")
        {
            if (item is MediaBrowser.Controller.Entities.TV.Season season && season.Series is not null)
                foreach (var p in season.Series.ProviderIds) providers["series:" + p.Key.ToLowerInvariant()] = p.Value;
            if (item is MediaBrowser.Controller.Entities.TV.Episode episode && episode.Series is not null)
                foreach (var p in episode.Series.ProviderIds) providers["series:" + p.Key.ToLowerInvariant()] = p.Value;
        }
        else foreach (var p in item.ProviderIds.Where(p => !string.IsNullOrWhiteSpace(p.Value)))
            providers[p.Key.ToLowerInvariant()] = p.Value;
        return new(item.Id, item.GetType().Name, item.Name, item.Path, item.ProductionYear, providers,
            item is MediaBrowser.Controller.Entities.TV.Season ? item.IndexNumber : item.ParentIndexNumber,
            item is MediaBrowser.Controller.Entities.TV.Episode ? item.IndexNumber : null);
    }
    /// <summary>Capture synchronously before accepting list additions.</summary>
    public void Capture(Guid owner, Guid id)
    {
        var user = users.GetUserById(owner); var item = library.GetItemById(id);
        if (user is null || item is null || !Supported.Contains(item.GetType().Name)) return;
        if (!item.IsVisibleStandalone(user) && !(item is Video && new CatalogAccess(library,users,data).Displays(owner,[id]).ContainsKey(id))) return;
        memory.Remember(owner, Identity(item));
    }
    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            ResetQuietPeriod();
            foreach (var row in memory.References().Where(r => r.CurrentItemId == e.Item.Id))
                memory.MarkMissing(row.Owner, row.CurrentItemId);
        }
        catch (Exception error) { log.LogError(error, "Unable to record catalog disappearance; existing PLM intent is retained."); }

    }
    private void OnUserDataSaved(object? sender, UserDataSaveEventArgs e)
    {
        // Playback defaults and imports are NOT an instruction to remove a favorite.
        if (e.SaveReason is not (UserDataSaveReason.UpdateUserRating or UserDataSaveReason.UpdateUserData)) return;
        // UpdateUserRating also covers thumbs-up/down. Only favorite routes
        // and DTOs explicitly carrying IsFavorite represent favorite intent.
        if (!NativeFavoriteIntent.HasFavoriteField) return;
        var favorite=e.UserData.IsFavorite;
        try
        {
            if (!Supported.Contains(e.Item.GetType().Name)) return;
            var user = users.GetUserById(e.UserId);
            if (user is null || !e.Item.IsVisibleStandalone(user)) return;
            var identity = Identity(e.Item);
            // Capture a fresh action on the destination BEFORE a background
            // rebind. A separate destination reference blocks stale replay.
            var row = memory.Remember(e.UserId, identity);
            memory.RecordFavorite(e.UserId, row.ReferenceId, favorite);
        }
        catch (Exception error) { log.LogError(error, "Unable to preserve favorite intent."); }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Cycle(stoppingToken);
                LastSuccessfulCycle = DateTimeOffset.UtcNow; LastError = null;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                LastError = error.GetType().Name;
                log.LogError(error, "Catalog resilience cycle failed; intent retained for retry.");
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
    private void Cycle(CancellationToken token)
    {
        if (library.IsScanRunning) ResetQuietPeriod();
        // Seed only previously unknown favorites. Catalog loss cannot clear
        // saved intent, and a later native true cannot erase a false tombstone.
        foreach (var user in users.GetUsers().ToArray())
        {
            token.ThrowIfCancellationRequested();
            var known = memory.References(user.Id);
            for (var offset = 0; ; offset += 200)
            {
                var page = library.GetItemsResult(new InternalItemsQuery { User = user, Recursive = true,
                    IsFavorite = true, IncludeItemTypes = Supported.Select(Enum.Parse<BaseItemKind>).ToArray(),
                    StartIndex = offset, Limit = 200, OrderBy = [(ItemSortBy.SortName, SortOrder.Ascending)] }).Items;
                foreach (var item in page.Where(i => i.IsVisibleStandalone(user)))
                {
                    var identity = Identity(item);
                    // The native core may already have restored this favorite.
                    // Do not create a competing reference before lists rebind.
                    if (!known.Any(r=>r.CurrentItemId==item.Id) && known.Any(r=>
                        library.GetItemById(r.CurrentItemId) is null && r.Identity.Matches(identity))) continue;
                    var row = memory.Remember(user.Id, identity);
                    memory.RecordFavorite(user.Id, row.ReferenceId, true, seedOnly: true);
                }
                if (page.Count < 200) break;
            }
        }
        foreach (var source in lists.Sources())
        {
            token.ThrowIfCancellationRequested();
            foreach (var selection in lists.Get(source.Owner, source.Id, true).Items)
                Capture(source.Owner, selection.ItemId);
        }
        BaselineComplete = true;
        var allowRebind = !library.IsScanRunning && DateTimeOffset.UtcNow - QuietSince >= TimeSpan.FromSeconds(60);
        foreach (var row in memory.References())
        {
            token.ThrowIfCancellationRequested();
            var user = users.GetUserById(row.Owner);
            if (user is null) continue; // Never adopt a recreated account by name.
            var current = library.GetItemById(row.CurrentItemId);
            if (current is null)
            {
                memory.MarkMissing(row.Owner, row.CurrentItemId);
                if (!allowRebind || row.Identity.Providers.Count == 0) continue;
                var candidates = FindCandidates(row, user);
                // Conservative: even a partially hydrated second version makes
                // the result ambiguous. Never choose the first item of a scan.
                if (candidates.Count != 1 || !candidates[0].IsVisibleStandalone(user)) continue;
                if (!memory.Rebind(row, Identity(candidates[0]))) continue;
            }
            else
            {
                if (!current.IsVisibleStandalone(user)) continue; // No new permission.
                if (allowRebind) memory.Reappeared(row, Identity(current));
            }
        }
        if (!allowRebind) return;
        foreach (var row in memory.References().Where(r => r.PendingFavorite && r.Favorite.HasValue))
        {
            token.ThrowIfCancellationRequested();
            var user = users.GetUserById(row.Owner); var item = library.GetItemById(row.CurrentItemId);
            if (user is null || item is null || !item.IsVisibleStandalone(user) || !row.Identity.Matches(Identity(item))) continue;
            using var lease=favorites.Acquire(row.Owner,row.CurrentItemId,token);
            if(!memory.IsPendingFavorite(row))continue;
            {
                // Preserve every playback field. PLM is not a playback-history
                // restore tool; only the favorite bit is replayed.
                var state = data.GetUserData(user, item);
                if (state?.IsFavorite != row.Favorite.GetValueOrDefault())
                {
                    data.SaveUserData(user, item, new MediaBrowser.Model.Dto.UpdateUserItemDataDto {
                        IsFavorite=row.Favorite.GetValueOrDefault()
                    }, UserDataSaveReason.UpdateUserRating);
                }
                memory.AcknowledgeFavorite(row);
            }
        }
    }
    private IReadOnlyList<BaseItem> FindCandidates(RememberedMedia row, Jellyfin.Database.Implementations.Entities.User user)
    {
        if (row.Identity.Kind is not ("Episode" or "Season"))
            return library.GetItemsResult(new InternalItemsQuery { User = user, Recursive = true,
                IncludeItemTypes = [Enum.Parse<BaseItemKind>(row.Identity.Kind)],
                HasAnyProviderId = HostProviders(row.Identity.Providers), Limit = 3 }).Items;
        var seriesProviders = row.Identity.Providers.Where(p=>p.Key.StartsWith("series:",StringComparison.Ordinal))
            .ToDictionary(p=>p.Key[7..],p=>p.Value);
        if (seriesProviders.Count==0 || row.Identity.Season is null) return [];
        var parents=library.GetItemsResult(new InternalItemsQuery { User=user,Recursive=true,
            IncludeItemTypes=[BaseItemKind.Series],HasAnyProviderId=HostProviders(seriesProviders),Limit=3 }).Items;
        if (parents.Count!=1) return [];
        return library.GetItemsResult(new InternalItemsQuery { User=user,ParentId=parents[0].Id,Recursive=true,
            IncludeItemTypes=[Enum.Parse<BaseItemKind>(row.Identity.Kind)],
            ParentIndexNumber=row.Identity.Kind=="Episode"?row.Identity.Season:null,
            IndexNumber=row.Identity.Kind=="Episode"?row.Identity.Episode:row.Identity.Season,Limit=3 }).Items;
    }
    private static Dictionary<string,string> HostProviders(Dictionary<string,string> providers) =>
        providers.ToDictionary(p=>Enum.TryParse<MetadataProvider>(p.Key,true,out var id)?id.ToString():p.Key,p=>p.Value);
}
