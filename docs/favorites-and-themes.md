# Saved hearts and native favorites

A filled heart means native favorite OR membership in any active list, including Watch Later. This is a presentation state only. Saving to a list never changes UserData.IsFavorite. Liked remains native Jellyfin data and is modified only by explicitly toggling its checkbox. Clicking any heart opens all memberships; removing the last membership empties the heart unless it is natively liked.

GET PLM/v1/me/items/memberships?ids=... returns matching IDs for the authenticated owner after visibility checks. Limit 200, private/no-store. The UI uses its own visual class, preserves data-isfavorite and batches lookups. Per-owner cache: 1000 entries, 60 seconds, invalidated after changes; stale responses are rejected. Native clients without this web integration retain native favorite behavior.

The erroneous favorites/reconcile POST and automatic favorite writes have been removed. Earlier development notes describing that behavior are superseded. Regression checks: web_memberships.py, web_smoke.py, web_locales.py and web_page_loader.py. Use PLM_BROWSER_CHANNEL=msedge or installed Playwright Chromium. Host themes and interface-language fallback remain supported.
