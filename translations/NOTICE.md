# Translation sources

Messages with a `native` key in messages.json reuse the corresponding Jellyfin Web
12.4.0 core translation, from https://github.com/jellyfin/jellyfin-web.
Those translations are copyright the Jellyfin Web contributors and licensed
GPL-2.0-or-later, as in that project's LICENSE. PLM is distributed under GPL-3.0.
Only the curated message subset is bundled; no media, branding or credentials are included.

The supported locale list is taken from the target release's lib/globalize/locales.ts,
rather than every JSON file in its strings directory. Regenerate with
`python scripts/build-locales.py PATH_TO_JELLYFIN_WEB_SRC`.

Plugin-specific English and Portuguese messages live in messages.json. Add overrides
in translations/<normalized-locale>.json. Missing messages fall back to English;
locale support does not mean complete human translation of plugin-specific messages.
