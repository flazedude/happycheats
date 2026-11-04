# Livin' the Cream loader notices

This payload includes the official BepInEx 5.4.23.5 Windows x64 distribution and its bundled HarmonyX, Harmony interop, Mono.Cecil, MonoMod and UnityDoorstop components. Corresponding license texts and the UnityDoorstop v4.5.0 source archive accompany the payload. The reused pinned loader preparation helper and `third-party-resources.json` record their official upstream resources and SHA-256 values.

The trainer uses the installed game's Newtonsoft.Json assembly at runtime; it does not redistribute that game-provided assembly. The synthetic test harness has a separate NuGet test dependency which is not shipped.

Game and Unity assemblies are local compile-time references and read-only compatibility inspection inputs. No game assembly, extracted game code, art or save data is distributed in source or this payload.
