# Nomad Drive loader notices

This payload includes the official BepInEx 5.4.23.5 Windows x64 distribution with its bundled HarmonyX, Harmony interop, Mono.Cecil, MonoMod and UnityDoorstop components. The accompanying license texts and UnityDoorstop v4.5.0 source archive are recorded with upstream URLs and pinned SHA-256 hashes in `third-party-resources.json`.

The plugin targets .NET Standard 2.1 and uses the game's existing Newtonsoft.Json and Unity/Mirror assemblies. Those game-provided libraries are local compile references and are not copied into the payload. No game assemblies, extracted game code or game data are distributed.

The source inspector uses Mono.Cecil 0.11.6 from NuGet for read-only metadata inspection. The temporary ilspycmd installation used during development is not a payload dependency and is not shipped.
