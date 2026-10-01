# Local hook compatibility checks

This standalone .NET 6 executable loads the already-built trainer and genuine locally installed Stardew Valley / SMAPI assemblies. It validates exact target signatures, checks the three item-use transpilers and the NPC gift transpiler with their failure guards, then asks Harmony to install and remove all ten patches. The gift checks require exactly one regular gift consumption call to change, leaving six special-offer/quest branches and every other instruction intact. Branch labels and exception metadata must survive rewriting.

Isolated helper checks also execute the real item-retention helper and toggle lookup with uninitialized placeholder objects, simulated inventory stacks of one and three, and temporary Harmony stubs for external game state and the original inventory reducer. They verify enabled gifts skip consumption and disabled, non-local, not-ready and uninitialized states retain ordinary consumption. These checks do not construct `Game1`, initialize a farm, invoke NPC gifting, open a save, or install a mod. The simulated inventory check is not live gameplay validation.

Build the trainer in Release first, then run from the repository root:

```powershell
dotnet run --project modules/stardew/tests/HappyCheats.StardewValley.HookChecks -c Release
```

Override local paths when needed:

```powershell
dotnet run --project modules/stardew/tests/HappyCheats.StardewValley.HookChecks -c Release -p:GameDirectory="D:\Steam\steamapps\common\Stardew Valley" -p:SmapiDirectory="C:\path\to\SMAPI" -- "D:\Steam\steamapps\common\Stardew Valley" "C:\path\to\SMAPI"
```

`ModAssembly` can likewise point at another already-built trainer DLL. The project copies local managed references to its ignored `bin` directory so the checks run independently. Those binaries are only for local verification; do not package or distribute this test output. This does not replace live gameplay testing.
