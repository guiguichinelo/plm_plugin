# Validation and limits

PLM beta 3 targets Jellyfin 12.4.0 and Linux x64. Its release checks include 15 core tests, 13 HTTP tests, browser workflows, navigation/title regression checks and all 64 active interface locales, including the personal settings heading and RTL/mobile overflow. Existing live checks use native and custom themes. This does not establish compatibility with other server releases, native clients or sustained production load.

Automated checks cover domain ownership and persistence, authenticated HTTP routes, revisions, private artwork, native mutation guards, visual list membership, native favorites remaining independent, cached navigation, template ownership, pagination, mobile layouts and locale fallback. See tests/ and the CI workflow for executable checks. Real browser checks confirmed that a list-only item has a filled visual heart while its native IsFavorite remains false.

Translations are partial outside English/Portuguese. Optional native playlist mirrors remain experimental. Back up your server and plugin database before upgrading.
