# First beta validation — 2026-11-03

This documents the first public beta, 0.1.0-beta.1. It is a prerelease, not a stable production release.

Automated checks:

- 15 core tests: owner isolation, revision conflicts, Watch Later idempotency, membership deduplication, order validation, soft deletion/restore, consistent SQLite backup, import atomicity, mirror ledger retention, account retention/recovery and owner-scoped membership queries.
- 10 HTTP tests: authentication, owner isolation, administrator scope and backup, pagination limits, native mutation/sharing guards including bulk IDs, ordinary playlist editing and membership-picker isolation.
- Headless Edge DOM workflow: native heart, independent list membership, management separation, trash/restore, safe list names, Watch Later position after Next Up, watched-item retention, legacy Favorites URL redirection and mobile layout. Both legacy and modern custom-element creation are covered. Synthetic API only; run `python tests/web_smoke.py` after installing Playwright. Set `PLM_BROWSER_CHANNEL=msedge` on Windows if desired.

Real Jellyfin 12.4.0 Linux x64 checks:

- Plugin and matching Plugin Pages/File Transformation dependencies loaded in an isolated container with separate empty configuration and internal Docker network.
- Three synthetic accounts and locally generated media; no production media mounted.
- Anonymous request: 401. Another account's collection or targeted mirror read: 404.
- Native catalog enumeration excluded the other account's mirror. Publishing, adding a share and bulk deletion of managed playlists were blocked. Ordinary playlists remained editable.
- Native watched status changed successfully and the PLM selection remained.
- Revoking library access retained the selection while redacting its metadata.
- Full-series selection expanded into three episodes in order S01E01, S01E02, S02E01.
- Browser integration on the homologation host: page/menu registration, authenticated list creation, rename, trash and restore, with no JavaScript errors. Custom theme and read-only web mount preserved.
- Revised Favorites interface checked against the real host: five native favorites and artwork, separate management, a Favorites entry in user preferences, native-item save picker and Watch Later directly after Next Up. A temporary movie selection proved list membership did not favorite the item; the temporary membership was removed afterward.

The latest theme alignment was saved through the supported branding configuration API, with the original configuration backed up. The consolidated UX3 package was subsequently installed on the homologation host after infrastructure access was restored. Real browser checks confirmed that both `#/home?tab=1` and `#/favorites` redirect to the new Favorites destination, its count matches all five native favorites, management is reachable from user preferences, Watch Later follows Next Up, and native playlists remain disabled with zero records. The installed mobile view was captured at 390 px without horizontal overflow. These checks produced no JavaScript errors; the separate production release and native-client playback gates below remain open.

Stable-release gates still open:

- Playback and navigation on actual Android TV, mobile and desktop native clients; the API tests do not substitute for these.
- Sustained load, large catalogs, mirror creation crash/fault injection, parallel updates and host upgrades beyond 12.4.0.
- Linux ARM64 and Windows runtime tests. The first public catalog distributes Linux x64 only.

The integration environment and operational backups are recorded privately outside this public repository. CI runs server and browser checks on main and pull requests; consult repository Actions for per-commit results.

## UX4 browsing validation

Additional core/HTTP tests cover bounded pages of a 1,503-selection list, owner-scoped thumbnails and image validation, thumbnail backup/cleanup, idempotent Watch Later membership updates, cross-account thumbnail access, filtered page counts and invalid filters. Total: 24 passing tests.

The synthetic headless browser workflow covers the heart picker, independent clock, sidebar creation, right-click rename/upload/delete, safe names, trash restoration, searchable multiple genres, sorting/filter dialogs and pages of 40/80 from 123 favorites, including title search. It also checks the legacy element API, home placement, watched retention, legacy Favorites routes and mobile overflow.

A real isolated Jellyfin 12.4 instance successfully processed 10,000 synthetic selection IDs: 40 returned records at the requested offset; the sorted native query returned only the accessible real fixture. Cross-account list/thumbnail reads returned 404 and repeated clock writes were idempotent. This checks correctness, not sustained-load capacity.

The installed homologation UI was checked with its actual session: five favorites preserved; sidebar create/rename/thumbnail/delete; multiple searchable genres; heart picker; quick clock restored to its initial active membership; trash deep-link and backdrop removal outside browsing; no JavaScript errors in this workflow. The temporary empty test list and its thumbnail were removed by exact ID. Genre options use the paginated user-scoped Genres endpoint because the legacy Items/Filters endpoint returned empty genres on the migrated catalog. Plugin Pages startup must complete before loading its transformed web bundles; a cached untransformed bundle after an early reload required targeted HTTP-cache refresh during validation.


## UX5 cached navigation and theme regression

The previous backdrop failure reproduced through the actual header logo → Home → Favorites route in three consecutive round trips, both with custom branding and with the native Jellyfin dark stylesheet. Each test used a fresh browser context without cookies, retaining the authorized local session credentials only in memory. The native-style test blocked custom branding CSS requests and removed its inline override; the default theme accent remained unset. The original session's cookies were not cleared. This rules out dirty cookies as the cause of this reproduced failure.

DOM evidence showed that Jellyfin clears `.backdropImage` on page transitions and caches the Plugin Pages DOM. The old controller did not reenter its initialization when that cached view became visible. UX5 separates native favorites/personal-list providers from page lifecycle and the owned backdrop. The layer no longer shares the cleared host class; theme filtering is adopted from the native image slot and background colors remain theme-owned.

After installation, the same three round trips passed in each fresh-context theme, without page reload and without JavaScript errors. Two additional round trips passed in the existing authenticated browser. Favorite/watch-later counts were unchanged. The synthetic browser regression retains and reuses the same page DOM, simulates native backdrop clearing and verifies that a delayed favorites response cannot paint over Home after navigation. It still exercises the full list/filter/pagination workflow. These checks validate the tested native dark and custom styles on Jellyfin12.4, not every third-party theme or future server release.


## UX6 native controls and interface language

Ten HTTP tests pass, including embedded locale resources/index and unknown-locale 404. Both headless browser suites pass. The language suite iterates all64 locales active in the target release, regional and English fallback, live preference changes, preserved user-defined names and RTL/mobile overflow. A delayed-shell case verifies that redirecting to a legacy Plugin Pages route waits for the native header. The full browsing suite still covers list actions, paging and cached-navigation regressions. CI includes both browser checks; consult repository Actions for hosted results.

Real-host checks used native per-user client language preferences in isolated contexts with a pt-BR browser: pt-br, en-us, fr, ar and ja resolved correctly. Search/filter/page-size actions and the Home/Favorites round trip passed. Mobile390px had no horizontal overflow; Arabic inherited RTL. Japanese also passed with branding CSS requests blocked and its stylesheet disabled, preserving React-owned DOM. All five final workflows recorded zero JavaScript errors. The PLM search and native title search matched computed font size, rounding and backgrounds in focused/unfocused states. The administrator page's translated controls loaded in a read-only check.

The default-theme test initially removed a React-owned style node and produced host reconciliation/lifecycle errors. The test was corrected to disable the sheet without removing its node; those runs are not counted as successful evidence. Fresh independent mobile pages avoid disturbing cached native scrollers through viewport changes. Translation coverage is partial outside English/Portuguese: core strings use the target locale, while untranslated plugin-specific strings fall back explicitly to English. See LOCALIZATION.md and the generated locale index; this is not a claim of full human translation into64 languages.


## First beta packaging

Release tag v0.1.0-beta.1 maps to the numeric Jellyfin assembly/catalog version
0.1.0.1. The public package is Linux x64 only and includes the complete SQLite
dependency set and license notices. It is built after committing the source;
its JSON receipt records that commit, target ABI12.4.0.0, MD5 and SHA256.
The catalog is maintained in Betterfin, with a separate manifest per host major.
Its targetAbi is a minimum accepted by Jellyfin, not a promise of future minor
compatibility. No production or native-client playback claim is added by release.
