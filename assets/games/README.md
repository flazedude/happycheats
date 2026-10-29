# Game artwork

These original game icons and wordmark logos identify the games supported by HappyCheats. The game names, logos, and artwork belong to their respective owners. Their use does not imply endorsement of HappyCheats.

The assets were collected on 26–27 September 2026 from the installed Steam client's cache or the corresponding publisher-supplied Steam CDN assets. They are embedded in the client so that the library and game banners work offline and on PCs without cached Steam artwork.

`*-icon.png` is the largest available frame from the game's original Steam shortcut ICO, converted to PNG without resizing or visual changes. Chinese Parents and Stardew Valley provide 32-pixel icons; the other icons are 256 or 512 pixels. `*-logo.png` is the original transparent Steam library logo, copied without modification. The ASTRONEER wordmark is white and intended for a dark background.

| Module | Steam app | Icon source | Logo source |
| --- | --- | --- | --- |
| `chinese-parents` | [Chinese Parents](https://store.steampowered.com/app/736190/) | Local `steam/games/39b171aa02801a94d02bd3d1b375678a688b1488.ico` | Local `appcache/librarycache/736190/logo.png` |
| `onimusha` | [Onimusha: Way of the Sword](https://store.steampowered.com/app/2638890/) | [Steam shortcut icon](https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/2638890/5eb7d3081d17f214b5b3c557933840c340814caa.ico) | [Steam library logo](https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/2638890/b67dd8218970ed7fc90ae774bbfe47c237ceaccc/logo.png) |
| `laundry` | [Laundry Store Simulator](https://store.steampowered.com/app/3150440/) | Local `steam/games/15246d5c00945b6ba3bcb3ef4c2cd59bbc34aea5.ico` | Local `appcache/librarycache/3150440/logo.png` |
| `astroneer` | [ASTRONEER](https://store.steampowered.com/app/361420/) | Local `steam/games/4988749c9eac9dffec5ef052bec757b4d3aa2bf0.ico` | Local `appcache/librarycache/361420/logo.png` |
| `stardew` | [Stardew Valley](https://store.steampowered.com/app/413150/) | Local `steam/games/913064b27b4b25e2c03cbd261a8b03fd67da693c.ico` | Local `appcache/librarycache/413150/logo.png` |
| `dawnwalker` | [The Blood of Dawnwalker](https://store.steampowered.com/app/3751260/) | [Steam shortcut icon](https://cdn.akamai.steamstatic.com/steamcommunity/public/images/apps/3751260/c9691197a229bd4a304c01a6afc6704d02c85bf7.ico) | [Steam library logo](https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/3751260/ff879d3360b28fea99280726bac7210110646575/logo.png) |
| `crimson-desert` | [Crimson Desert](https://store.steampowered.com/app/3321460/) | Local `appcache/librarycache/3321460/f260267377759a5fea6b651817c9a413c6b562b0.jpg`, 32-pixel original converted to PNG without resizing | Local `appcache/librarycache/3321460/93af97ca3c73ab8db023948f93b23ef32113f38a/logo.png` |
| `black-myth-wukong` | [Black Myth: Wukong](https://store.steampowered.com/app/2358720/) | Local `steam/games/0121f85746c30090f72e2ba7eb5528fdfffa70b6.ico`, original 256-pixel frame | Local `appcache/librarycache/2358720/logo.png` |

Local source paths are relative to the Steam installation directory. Remote asset hashes were verified against the installed Steam client's `appcache/appinfo.vdf` game metadata.

The project links these PNGs as WPF resources under `Assets/Games/`. `GameArtwork.IconUri(moduleId)` supplies library icons, and `GameArtwork.LogoUri(moduleId)` supplies the selected game's banner logo.

Elden Ring ([Steam app 1245620](https://store.steampowered.com/app/1245620/)): icon from local `appcache/librarycache/1245620/b6e290dd5a92ce98f89089a207733c70c41a1871.jpg`, converted to PNG without resizing; original transparent logo from `appcache/librarycache/1245620/logo.png`, unchanged. FromSoftware / Bandai Namco artwork, collected 1 October 2026 and bundled as `elden-ring-icon.png` and `elden-ring-logo.png`.
`red-dead-redemption-2` (Steam 1174180): icon from local `appcache/librarycache/1174180/5106abd9c1187a97f23295a0ba9470c94804ec6c.jpg`, converted to PNG without resizing; logo from local `appcache/librarycache/1174180/logo.png`, unchanged. Rockstar Games artwork.

GTA V Enhanced (Steam app 3240220): icon from local appcache/librarycache/3240220/8355a7bbdb704f727bfba80ec56bc7228991338e.jpg, converted to PNG without resizing; original logo from appcache/librarycache/3240220/b45f1fc647bfb9389a2c741686627ef7e139f2df/logo.png. Collected 27 September 2026 and bundled as gta-v-enhanced-icon.png and gta-v-enhanced-logo.png.

Project Hospital (Steam app 868360): icon from local `appcache/librarycache/868360/65f5c77e94924d747c501a51746d9e8c243e4d45.jpg`, converted to PNG without resizing; original logo from local `appcache/librarycache/868360/logo.png`, unchanged. Collected 27 September 2026 and bundled as `project-hospital-icon.png` and `project-hospital-logo.png`.

Hotel Architect (Steam app 1602000): icon from local `appcache/librarycache/1602000/6aa8037112d19317351dd1f349cc225edbb49fef.jpg`, converted to PNG without resizing; original logo from `appcache/librarycache/1602000/12429f824c7451124a8902aba853e81782c8173b/logo.png`, unchanged. Collected 27 September 2026 and bundled as `hotel-architect-icon.png` and `hotel-architect-logo.png`.

Civilization VI (Steam app 289070): icon from local `appcache/librarycache/289070/9dc914132fec244adcede62fb8e7524a72a7398c.jpg`, converted to PNG without resizing; original logo from `appcache/librarycache/289070/c2743b863b6dc3f197b45effc18b95a944eff604/logo.png`, unchanged. Collected 27 September 2026 and bundled as `civilization-vi-icon.png` and `civilization-vi-logo.png`.

The Witcher 3: Wild Hunt — Remastered ([Steam app 292030](https://store.steampowered.com/app/292030/)): icon from local `appcache/librarycache/292030/7b52d11edee478d652ef4e7103e00644bed2c74c.jpg`, converted to PNG without resizing; original logo from `appcache/librarycache/292030/cd2760d950f55da9bc44f8d7feafe8f74097adb4/logo.png`, unchanged. CD PROJEKT RED artwork, collected 29 September 2026 and bundled as `witcher-3-icon.png` and `witcher-3-logo.png`.

Civilization VII ([Steam app 1295660](https://store.steampowered.com/app/1295660/)): icon from local `appcache/librarycache/1295660/23a2f48017d676c267ed51de4ca48ec97abf7de2.jpg`, converted to PNG without resizing; original transparent logo from `appcache/librarycache/1295660/f48b563972469c31a752d73075714b1cf4855926/logo.png`, unchanged. Firaxis Games / 2K artwork, collected 1 October 2026 and bundled as `civilization-vii-icon.png` and `civilization-vii-logo.png`.

Phantom Blade Zero (Steam app 4115450): original 32x32 cached Steam icon ce420dc458dd3d7af276f48fbaa44a05876760dc.jpg converted to PNG without resizing; transparent library logo 31e7af85afd14d7c0881a565b5b205af5b87a81c/logo.png copied unchanged. S-GAME artwork, collected 29 October 2026.
