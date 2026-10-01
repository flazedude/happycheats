# Game artwork

These original game icons and wordmark logos identify the games supported by HappyCheats. The game names, logos, and artwork belong to their respective owners. Their use does not imply endorsement of HappyCheats.

The assets were collected on 26 September 2026 from the installed Steam client's cache or the corresponding publisher-supplied Steam CDN assets. They are embedded in the client so that the library and game banners work offline and on PCs without cached Steam artwork.

`*-icon.png` is the largest available frame from the game's original Steam shortcut ICO, converted to PNG without resizing or visual changes. Chinese Parents and Stardew Valley provide 32-pixel icons; the other icons are 256 or 512 pixels. `*-logo.png` is the original transparent Steam library logo, copied without modification. The ASTRONEER wordmark is white and intended for a dark background.

| Module | Steam app | Icon source | Logo source |
| --- | --- | --- | --- |
| `chinese-parents` | [Chinese Parents](https://store.steampowered.com/app/736190/) | Local `steam/games/39b171aa02801a94d02bd3d1b375678a688b1488.ico` | Local `appcache/librarycache/736190/logo.png` |
| `onimusha` | [Onimusha: Way of the Sword](https://store.steampowered.com/app/2638890/) | [Steam shortcut icon](https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/2638890/5eb7d3081d17f214b5b3c557933840c340814caa.ico) | [Steam library logo](https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/2638890/b67dd8218970ed7fc90ae774bbfe47c237ceaccc/logo.png) |
| `laundry` | [Laundry Store Simulator](https://store.steampowered.com/app/3150440/) | Local `steam/games/15246d5c00945b6ba3bcb3ef4c2cd59bbc34aea5.ico` | Local `appcache/librarycache/3150440/logo.png` |
| `astroneer` | [ASTRONEER](https://store.steampowered.com/app/361420/) | Local `steam/games/4988749c9eac9dffec5ef052bec757b4d3aa2bf0.ico` | Local `appcache/librarycache/361420/logo.png` |
| `stardew` | [Stardew Valley](https://store.steampowered.com/app/413150/) | Local `steam/games/913064b27b4b25e2c03cbd261a8b03fd67da693c.ico` | Local `appcache/librarycache/413150/logo.png` |
| `dawnwalker` | [The Blood of Dawnwalker](https://store.steampowered.com/app/3751260/) | [Steam shortcut icon](https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/3751260/c9691197a229bd4a304c01a6afc6704d02c85bf7.ico) | [Steam library logo](https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/3751260/ff879d3360b28fea99280726bac7210110646575/logo.png) |

Local source paths are relative to the Steam installation directory. Remote asset hashes were verified against the installed Steam client's `appcache/appinfo.vdf` game metadata.

The project links these PNGs as WPF resources under `Assets/Games/`. `GameArtwork.IconUri(moduleId)` supplies library icons, and `GameArtwork.LogoUri(moduleId)` supplies the selected game's banner logo.

Added on 27 September 2026 for the combined 0.3.26 release: Project Hospital (868360), Hotel Architect (1602000) and Civilization VI (289070). Their original Steam library icons were converted from cached JPEGs to PNG without resizing; their original transparent library logos were copied unchanged. Exact source paths are recorded in `assets/games/README.md` in the development workspace. These assets are embedded in both client distribution formats.
