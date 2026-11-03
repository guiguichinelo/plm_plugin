# Interface language and native controls

PLM follows `html.lang`, which Jellyfin resolves from the current user's interface
preference before falling back to the browser language. It does not use the server's
metadata language, content language or a fixed Portuguese locale. Changing that
resolved language refreshes static/dynamic labels; unfinished PLM dialogs close so
they cannot retain labels from a previous language. User list names, titles and
catalog genres are user/catalog data and are not machine-translated.

The target Jellyfin Web 12.4.0 release exposes **64 interface locales** in
lib/globalize/locales.ts. The larger strings directory also contains inactive
translations; its file count is not a supported-locale count. Only the active list
is bundled. Common strings reuse the corresponding core dictionary. PLM-specific
messages have English and Portuguese entries; additional community translations
belong in translations/<locale>.json. **This is not complete translation of all
plugin messages into 64 languages.** Missing entries use English, never Portuguese
for a non-Portuguese locale. The generated index reports coverage per locale.

Resolution order: exact normalized locale, base language, en-us. Underscores and
case normalize to the Jellyfin form. Interface changes are observed at the host's
resolved locale, avoiding a second user preference store. Number/date formatting
uses Intl with that locale. RTL direction comes from the host and uses logical
spacing/alignment in PLM.

The list search uses native emby-input/searchfields-txtSearch styling, without a
magnifier or visible input label. The page-size selector
uses native emby-select-withcolor, selectLabel and selectArrow markup, with plain
numeric choices and a compact inline "Per page" label. The controls share a center
line with the browsing filters on desktop and wrap together on mobile. They
inherit the installed theme rather than PLM input colors.
The search is a plain input, like the host React Input; registering it as the
legacy `is=emby-input` while preassigning its class skips label initialization in
the host polyfill and must be avoided.

Regenerate catalogs from the matching checkout:

```sh
python scripts/build-locales.py /path/to/jellyfin-web/src
PLM_BROWSER_CHANNEL=msedge python tests/web_locales.py
PLM_BROWSER_CHANNEL=msedge python tests/web_smoke.py
```

Playwright Chromium can be used by omitting PLM_BROWSER_CHANNEL. web_locales.py
checks every active locale, regional/base/English fallback, live locale changes,
unchanged user list names, numeric page sizes and RTL/mobile overflow. It checks
key coverage against all scripts/templates. HTTP tests verify the packaged locale
index, actual dictionary resources and unknown-locale 404 responses.

Actual-host checks additionally set the native per-user client preference in fresh
isolated browser contexts, keeping the browser in pt-BR. No server branding,
metadata preferences or the original browser's user preferences are modified.
