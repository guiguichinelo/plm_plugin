# Catalog resilience (beta 5)

PLM keeps personal-list selections and native favorite intent independently of
the current Jellyfin item ID. Losing an item during a scan is not an instruction
to remove it from a list or to unlike it.

Available from beta 5, targeting Jellyfin 12.4.0 on Linux x64.
Earlier packages remain unchanged.

## Behavior

- List additions capture identity synchronously. A background baseline captures
  existing list entries and native favorites while their items are accessible.
- Identity and per-user favorite intent live in additive tables in plm.db.
  The existing administrative database backup includes them.
- An explicit unlike is remembered as false. Catalog absence, playback events,
  imports, and partial UserData updates that omit IsFavorite cannot clear intent.
- Reconciliation waits until the library scan is idle and the quiet interval
  has elapsed. A unique, visible candidate of the same media kind must share a
  nonempty external provider ID and have no conflicting common providers.
- Matching an episode or season additionally requires a unique parent series
  and matching season/episode numbers, including season zero for specials.
- A successful transaction updates the affected list selections and collection
  revisions, keeps order, original dates and deletion markers, and schedules
  native favorite replay separately. Retries are idempotent.
- Favorite replay changes only IsFavorite through the Jellyfin service. It
  preserves the current playback state and does not like an item just because
  it belongs to a list.
- Multiple copies, conflicting identities, insufficient metadata, and competing
  destination references remain pending. There is no title-only or path-only
  automatic match.

## Boundaries

Protection requires the plugin to have observed an item before its identity
disappears. Installing it after an outage cannot reconstruct an unknown identity.
The baseline must complete before destructive scan tests. It can protect future
rebuilds; it is not a historical-data recovery tool.

Supported capture kinds are Movie, Series, Season, Episode, Video, MusicVideo,
Audio, MusicAlbum, AudioBook and Book. Items lacking suitable external identity
remain stored but cannot be automatically matched to a different ID. External
metadata must eventually become available and correct for those matches.

The plugin does not prevent a native scan from deleting catalog entries, make
remote mounts permanently available, or back up the whole Jellyfin database.
It does not restore playback history lost by the host; its favorite replay
preserves whatever playback state the current item has.

All references are scoped by user GUID, never username. Candidate queries use
the current user's access. Missing accounts are not reassigned; remembered data
is removed after the existing 30-day missing-owner grace period. Administrative
health exposes counts, not another user's titles or preferences.

GET /PLM/v1/admin/resilience requires elevation and reports baseline completion,
the last successful cycle/error, protected references, missing references,
references with provider identity, and pending favorite writes.

## Validation

Run the core and HTTP suites:

    dotnet test tests/Plm.Core.Tests/Plm.Core.Tests.csproj -c Release
    dotnet test tests/Plm.Api.Tests/Plm.Api.Tests.csproj -c Release

Core coverage includes repeated ID changes, order/date preservation, optimistic
revision conflicts, owner isolation, deletion/unlike tombstones, competing
destination intent, delayed metadata, conflicting providers, special episodes,
and restart/backup persistence.

Before deployment, use a disposable Jellyfin with synthetic local media and
the exact server version. Seed favorites, personal lists and Watch Later; remove
the fixture catalog, change paths to force new IDs and restore it. Confirm
automatic rebinding without a restore operation. Also test missing metadata,
duplicate candidates, unlikes/deletions, and partial playback-only updates.
Never run destructive catalog-loss tests against real libraries.
