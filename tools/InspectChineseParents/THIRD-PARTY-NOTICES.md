# Chinese Parents loader components

The trainer payload carries unmodified binaries from the official BepInEx Windows x86 5.4.23.5 release. The game installation owns its Unity and Newtonsoft.Json libraries; those libraries are not redistributed by this payload.

| Component | Version/source | License file |
| --- | --- | --- |
| BepInEx | 5.4.23.5 | BepInEx-LICENSE.txt (MIT) |
| HarmonyX | 2.9.0 | HarmonyX-LICENSE.txt and Harmony-LICENSE.txt (MIT) |
| BepInEx.Harmony / HarmonyX interop | d4cdcb4cdeac14a0b77012165f5f5a9f5032a9fa, the release's pinned submodule | BepInEx.Harmony-LICENSE.txt (MIT) |
| Mono.Cecil | 0.10.4 | Mono.Cecil-LICENSE.txt (MIT) |
| MonoMod.RuntimeDetour / MonoMod.Utils | 22.1.29.1 | MonoMod-LICENSE.txt (MIT) |
| Unity Doorstop | 4.5.0 | UnityDoorstop-LICENSE.txt (LGPL 2.1) |

The complete Unity Doorstop 4.5.0 source archive is included alongside its license as `UnityDoorstop-v4.5.0-source.zip`; its README contains build instructions. The `winhttp.dll` loader is an unmodified separate dynamically loaded library. Users may replace it with a compatible rebuilt library; the launcher's managed install integrity checks will report a modified file and will not overwrite or remove it silently. Disable/remove the managed trainer installation before maintaining custom loader files.

`third-party-resources.json` lists each upstream source URL and verified SHA-256. The pinned BepInEx archive and license hashes are in `Prepare-BepInEx.ps1`. No license or source archive in this directory is installed into the game's folders by the package manifest; retain this directory when sharing a built payload.
