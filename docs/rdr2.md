# Red Dead Redemption 2

HappyCheats provides an experimental Story Mode menu for the Windows Steam installation (app 1174180), targeting executable version **1.0.1491.50**. Trainer version **0.1.0** is included in both the complete LAN update and standalone EXE. Compilation and offline fixtures do not establish actual gameplay compatibility.

Official **Red Dead Online is unavailable in this release**. ScriptHookRDR2's author states that it does not work in Red Dead Online and closes the game when entering multiplayer. The plugin additionally refuses online sessions. This is a Story Mode backend, not an Online menu.

## Setup

1. Close RDR2. Open HappyCheats, select **Red Dead Redemption 2**, then **Install / update**. It discovers Steam or accepts a selected game folder and checks executable version **1.0.1491.50**.
2. If required, HappyCheats downloads **ScriptHookRDR2 1.0.1491.17** and its original **dinput8.dll ASI loader** directly from the [author](https://www.dev-c.com/rdr2/scripthookrdr2/), verifies the archive, file hashes and x64 exports, and installs them with the trainer. First setup requires internet; an already installed, verified runtime can be reused offline. Unknown or modified loaders are preserved and block setup with a conflict message.
3. Launch normally and load **Story Mode**. Keep HappyCheats open and use its seven controls. No game or save was opened by the development/release process.

The installer preserves other mods and modified trainer files. It installs `HappyCheats.Rdr2.asi`, a bridge readme and an ownership receipt, plus the runtime, loader and author's readme when needed. The sample NativeTrainer is not installed. A failed transaction rolls back its writes while preserving concurrent changes. **Remove trainer** removes only unchanged trainer files it owns; it leaves ScriptHookRDR2, its loader, author's readme and unrelated mods in place. Removing HappyCheats alone does not remove other Story Mode mods or make an installation suitable for Online.

## Latest version and ScriptHook source

On **27 September 2026**, a fresh SteamCMD anonymous query for app **1174180** reported public build **13773296**, matching the installed game's executable **1.0.1491.50**. This is the current Steam build checked for this release, not a requirement to downgrade. Other storefront executables and future patches need separate verification.

The author's current ScriptHook release is numbered **1.0.1491.17**. Its documentation describes automatic address detection for later game patches until the script system changes; an older runtime version number alone does not establish incompatibility with **1.0.1491.50**. Actual startup and gameplay still require testing in RDR2.

The official SDK supplies API headers, an import library and sample plugin source, but no runtime implementation source. There is therefore no official runtime source here to rebuild. The SDK prohibits redistributing the runtime DLL with plugins, so HappyCheats integrates setup through a direct download instead of embedding that DLL in either release package. Runtime hashes and provenance are recorded in `Rdr2RuntimePackage.cs` and the [module documentation](../modules/rdr2/README.md).

## Controls

| Control | Behavior |
| --- | --- |
| Unlimited health | Refill the current player's health and health core. A lethal hit between refills can still kill. |
| Unlimited stamina | Refill the current player's stamina and stamina core. |
| Unlimited Dead Eye | Refill the player's Dead Eye ring and core. |
| Infinite clip | Enable the game's infinite-clip flag. Reserve ammunition is not granted. |
| Horse health | Refill the active mount's health and core. It does not grant invincibility. |
| Horse stamina | Refill the active mount's stamina and core. |
| Clear wanted | Clear current wanted status once. This does not promise to remove recorded regional bounties. |

Use the HappyCheats buttons; this initial payload does not add keyboard shortcuts or an in-game overlay. Toggles begin off, clear when player/session readiness is lost, and horse controls require a living active mount. **Disable all** remains usable for connected cleanup. Refills and one-shot wanted clearing are not undone when disabling. Normal game saving may persist their effects.

Commands carry a session identity, expiry and acknowledgement. Stale/mismatched status cannot enable controls, and a timeout does not automatically retry an action.

## Validation limits

The installed game was inspected read-only: Steam build **13773296**, executable **1.0.1491.50**. The inspected folder had an existing `Rampage.asi` but lacked ScriptHookRDR2 and the ASI loader; it was left untouched. Offline checks cover runtime downloads and malformed responses, archive and export checks, transactional setup/rollback, native command/state logic, unsupported hosts, loader ABI, installer ownership, protocol freshness and the English/Chinese menu. Loader startup in RDR2, actual cheat effects, other-mod interactions and Online functionality have not been verified. No games were started, injected into, or used to test this release, and no saves were edited.

Native API references and runtime ABI evidence are recorded with the trainer sources under `modules/rdr2/native/`.
