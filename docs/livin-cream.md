# Livin' the Cream: Ice Cream Simulator

Independent cheat **1.0.1** supports Steam app **4621810**, inspected build **26511230**, with HappyCheats **0.3.69+**. The game changed from Mono to **IL2CPP (metadata 39)**. This update replaces the managed loader with a native trainer; the client EXE and version stay unchanged. Package 1.0.0 remains an immutable historical release for Mono build 26415994.

## Update and connect

Close the game, open **Update** beside Library Sort, and update Livin' the Cream. The operation downloads, verifies and installs the package. Launch normally, load your shop, then click **Connect trainer / 连接修改器**. Each new game process requires explicit connection. Discovery never injects a DLL. Keep HappyCheats open while using continuous effects.

Client 0.3.69 hides absent games in its update list. For first installation, close HappyCheats and the game and run `scripts/Install-LivinCream.cmd` from this checkout (PowerShell 7 and .NET 8 SDK required). `scripts/Install-LivinCream.ps1 -InspectOnly` checks prerequisites without writes; `-ClientDirectory` and `-GameDirectory` select folders. The helper uses the signed catalog, package store and module installer. Reopen your existing client afterward. Later versions use normal Library Update. No helper starts/closes games or replaces the client EXE.

## Controls

| Control | Behavior |
| --- | --- |
| Connect trainer | Verify the exact native assembly, metadata, installed trainer and process before explicit connection. |
| Money | Set once to a whole number from 0 to 100,000,000 using the game's setter. |
| Add shop XP | Grant 1–10,000 once through LevelManager.AddXP, preserving normal level-up rewards. |
| Add skill points | Grant 1–1,000 once through SkillTreeManager.EarnSkillPoint, preserving earned-point accounting. |
| Unlimited customer patience | Stop the front customer's timer and hide its patience UI. Off refreshes native play-style rules and starts a full normal timer where applicable. Classic/Cozy mode stays unchanged. |
| Unlimited soft-serve stock | Prevent consumption of existing reservoir contents in the current shop scene. Fill machines normally first. Does not create flavors or other supplies. |
| Refill soft-serve stock | Refill nonempty reservoirs once through RestoreStock, preserving flavors and updating visuals. Empty reservoirs remain empty. |

Grants and refill change loaded state and may persist through normal saves. Turning a toggle off does not undo prior grants/refill. The trainer never invokes saving or edits save files.

Use the shared **Ctrl+Del** menu or, with Xbox shortcuts enabled, **hold Menu (☰) for 350 ms then tap RB**. Mouse/keyboard operate controls. Use windowed or borderless play. No separate renderer or game-specific controller profile is installed.

## Migration and ownership

For a receipt-owned 1.0.0 installation, unchanged old HappyCheats plugin and Mono bootstrap files move to `happycheats-recovery/livin-cream-mono-<id>/` before native installation. Recovery files and the old receipt remain available. Failure rolls moves back when safe. Shared BepInEx core files remain on disk but are no longer loaded by the removed Mono bootstrap.

Modified files, a bootstrap shared with other plugins/patchers, or an unowned Mono loader stop migration with an actionable error. Resolve those mods deliberately and retry the same update. The native trainer has its own ownership receipt; removal preserves modified and preexisting files.

## Compatibility and validation

Exact SHA-256 identities are enforced at inspection, installation, connection and native startup:

- GameAssembly.dll: `c6081c74041250a89bf8cb943b5e482af8a05a15853dc727193ffd6fcf51f168`
- global-metadata.dat: `f19c0af79b93122307211e81d2f2c2deaeb09f908670643f687f83773062d137`

The DLL verifies four hook prologues and runtime method identities before activation. Gameplay calls run from the existing game input tick on the game thread. Readiness requires a single active player, their shop, level/skill managers, restored database identities, a fresh game frame and an authorized client lease. Load transitions rotate the command session and clear confirmed effects. The shared client retains requested toggles and restores them through new ready-session commands. Off/Disable all cancels restoration. One-shot grants never replay; a new process requires Connect again.

Inspection checks the x64 PE, metadata version, 23 exported APIs and a reviewed record of 28 methods, eight fields and four distinct hook sites. Cpp2IL is local inspection tooling only; generated assemblies/code are ignored and never shipped. Native fixtures cover expiry, stale sessions, bounded progression, lease/context changes and actual prologue relocation. Module tests cover explicit connection, passive context renewal, no automatic injection, package integrity and legacy migration/rollback. Shared menu/controller and toggle-continuity regressions apply.

Validation is **offline/static/synthetic**. The packaged DLL is smoke-tested in an unsupported disposable host. Gameplay effects, in-game startup, physical-controller operation and exclusive fullscreen remain untested. No game was launched or save changed. Historical Mono source/fixtures remain for 1.0.0 reference and are no longer shipped by the native builder.
