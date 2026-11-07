# Beta 7 fixes

Slow native image downloads, playback probes and metadata lookups no longer serialize unrelated Jellyfin commands. The mirror creation barrier applies only to structural item/playlist writes while mirror creation is enabled. Existing mirror ownership checks remain active, including read routes, bulk operations and cross-account requests.

Unavailable selections now show their remembered title when it exists in the current user's recovery data, and an accessible **Remove from list** button. Removal uses the private list API, works without a live catalog item, preserves other lists, and retains the existing trash/restore behavior. A later catalog rebind does not undo a user's deletion. Stale revisions remain conflicts rather than overwriting another session.

Watch Later buttons also stop inheriting native rating-button lifecycle selectors. In-place details refreshes after editing or identifying an item can complete without calling `setItem()` on a plain plugin button.

Validation includes API concurrency and ownership tests, unavailable selection removal/restoration/rebind tests, browser pagination/conflict/locale checks, native details refresh, and all supported interface locales. No Jellyfin database schema migration is required.
