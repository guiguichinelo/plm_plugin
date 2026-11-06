# Jellyfin Personal Library Manager

PLM adds private personal collections and Watch Later to Jellyfin. Users select existing catalog content, organize it, filter by genre/progress and remove selections without deleting original files. Native Favorites remain available alongside collections.

**Beta 6: 0.1.0-beta.6, targeting Jellyfin 12.4.0 on Linux x64.** Other server versions and architectures are not validated. See [release notes](docs/releases/0.1.0-beta.6.md), [compatibility](docs/COMPATIBILITY.md) and [architecture / data lifecycle](docs/ARCHITECTURE.md).

![PLM — Personal Library Manager](https://raw.githubusercontent.com/guiguichinelo/betterfin/main/assets/plm/thumbnail.png)

Add [Betterfin's Jellyfin 12 manifest](https://raw.githubusercontent.com/guiguichinelo/betterfin/main/12/manifest.json) in **Dashboard → Plugins → Repositories**, then install **Personal Library Manager**. This catalog currently distributes the Linux x64 beta only. Install matching Plugin Pages/File Transformation dependencies first. [Download the beta](https://github.com/guiguichinelo/plm_plugin/releases/tag/v0.1.0-beta.6).

Features implemented:

- Multiple owner-scoped lists and an idempotent default Watch Later.
- Add/remove, recoverable deletion, rename, private list thumbnails and contextual list actions.
- Server pagination, search, searchable multiple genres and type/progress/year/favorite filters.
- Native Favorites integration, user JSON export/import and administrator database backup.
- Plugin Pages user interface that inherits the installed web theme, without bundle edits.
- Native search/page-size controls and interface-language preference support; 64 target-release locales with explicit English fallback for untranslated plugin messages. See [localization coverage](docs/LOCALIZATION.md).
- Optional automatic private playlist mirrors, including series/season expansion and native mutation protection.
- Thirty-day deletion eligibility, maintenance preview/execute and suspension of missing accounts.

## Build and test

Install the .NET SDK selected by `global.json`:

```powershell
dotnet test tests/Plm.Core.Tests/Plm.Core.Tests.csproj
dotnet test tests/Plm.Api.Tests/Plm.Api.Tests.csproj
./scripts/package.ps1 -Runtime linux-x64
```

The package, MD5/SHA256 receipt and SHA256SUMS.txt are generated in `artifacts/`. CI builds/tests and saves artifacts; publication remains a separate step. Keep every packaged SQLite dependency and the RID-specific native library together. Source/version and archive checksums for catalog maintainers are described in the release notes.

## Manual beta installation

1. Back up your Jellyfin configuration and PLM database. Use an isolated test instance first.
2. Stop Jellyfin. Move any previous PLM plugin directory outside `plugins` and extract this package into `plugins/Personal Library Manager_0.1.0.6` inside its configuration directory, preserving file ownership. Keep PLM's data directory. Do not combine packages for different architectures or keep two enabled versions.
3. Install Plugin Pages and File Transformation builds matching **12.4.0**, then start Jellyfin.
4. Open **Favoritos** to browse Curtidos, Watch Later and private lists. List administration is also available through **Favoritos** in user preferences. The plugin administrator page contains maintenance and the opt-in switch for native mirrors.
5. Validate privacy with two non-administrator test accounts before enabling mirrors on a populated server. Test your actual clients; support for native playlist content varies.

PLM stores its independent database at `<Jellyfin DataPath>/plm/plm.db`. The normal shared catalog stays accessible. Photos, books and other content require corresponding host/client support; a collection does not add a missing player to a native client.

## API

All data routes use the existing Jellyfin authentication. `/PLM/v1/me` never accepts an owner user ID. List edits require the current collection `revision`; stale clients receive `409`. The quick Watch Later setter is idempotent and edits one membership transactionally. Thumbnail writes replace only the authenticated owner's image.

- `collections`, `watch-later`, `collections/{id}`, `collections/{id}/items`, `order`, `move`, `restore`
- `preferences`, `export`, `import`
- `thumbnails`, `lists/{key}/thumbnail`, `watch-later/membership`, `watch-later/items/{id}`
- Administrator only: `/PLM/v1/admin/health`, `backup`, `maintenance/preview`, `maintenance/execute`

Keep infrastructure addresses, credentials, private branding and real user exports out of this public repository. GPL-3.0; see [LICENSE](LICENSE).

## Catalog resilience

Beta 5 preserves personal-list membership and native favorite intent
across catalog disappearance and item-ID changes, with conservative matching
and explicit unlike/deletion markers. See [behavior and validation boundaries](docs/CATALOG-RESILIENCE.md).
Wait for the initial baseline before testing catalog loss. Unidentified or ambiguous matches remain pending.
