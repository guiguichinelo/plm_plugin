# Compatibility and release policy

| Component | Target | Evidence |
| --- | --- | --- |
| Jellyfin Server | 12.4.0 | NuGet Controller/Model 12.4.0; Linux x64 container load tested |
| .NET | 10; SDK 10.0.403 | Local builds and automated tests |
| Plugin Pages | 3.0.3.0, Release-12.4.0.zip | Page registration checked in isolated host |
| File Transformation | 3.0.3.0, Release-12.4.0.zip | Matching dependency installed in isolated host |
| Jellyfin 10.11.x | Unsupported | Different host/runtime ABI |
| Other 12.x versions | Unverified | Require rebuild and host integration tests |
| Windows x64 / Linux ARM64 | Build targets only | No runtime compatibility claim yet |

Dependencies are loaded through Plugin Pages' documented reflection interface. Backend APIs do not require the two web integration plugins. PLM never patches installed web bundles; menu integration is performed by Plugin Pages/File Transformation.

Do not set a broad manifest target to imply all future 12.x versions work. A release needs its own exact host build and integration matrix, including restricted libraries, private playlists, disabled users, native mutation variants, pagination, restore and series expansion. Successful compilation is not proof of client playback, privacy on every query or load capacity.

Source references:

- [Jellyfin host source matching NuGet 12.4.0](https://github.com/jellyfin/jellyfin/tree/83d0d0e7f76e23e2e085c8ff67d28b772d35967d)
- [Plugin Pages registration interface](https://github.com/IAmParadox27/jellyfin-plugin-pages#the-plugin-way-3000)
- [Plugin Pages releases](https://github.com/IAmParadox27/jellyfin-plugin-pages/releases/tag/3.0.3.0)
- [File Transformation releases](https://github.com/IAmParadox27/jellyfin-plugin-file-transformation/releases/tag/3.0.3.0)
