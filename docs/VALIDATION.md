# Validation and limits

PLM beta 2 was tested with Jellyfin 12.4.0, Linux x64 and a browser using both native and custom themes. This does not establish compatibility with other server releases, native clients or sustained production load.

Automated checks cover domain ownership and persistence, authenticated HTTP routes, revisions, private artwork, native mutation guards, visual list membership, native favorites remaining independent, cached navigation, template ownership, pagination, mobile layouts and locale fallback. See tests/ and the CI workflow for executable checks. Real browser checks confirmed that a list-only item has a filled visual heart while its native IsFavorite remains false.

Translations are partial outside English/Portuguese. Optional native playlist mirrors remain experimental. Back up your server and plugin database before upgrading.
