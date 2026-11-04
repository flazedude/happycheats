# Project Hospital

Independent Project Hospital package **1.0.1** contains trainer **0.1.2** for Windows Steam app **868360**, inspected build **21161440**. Compatible HappyCheats clients **0.3.65+** load it without a client/EXE update. In Library, choose **Update** beside Sort and update Project Hospital with supported games closed. On clients 0.3.65–0.3.66, reload the Library, then choose the game's **Install / update**. Newer clients complete those steps through the existing update flow. The module checks the installed game assembly SHA-256 before installation or connection; other builds require inspection.

Close Project Hospital, select it in HappyCheats, choose **Install / update**, then launch the game. The package includes the pinned BepInEx **5.4.23.5 x64** loader and the trainer, so setup does not need an additional download. Load a hospital for hospital controls; campaign unlocking is available at the main menu once the profile is loaded. Reopen the campaign selection screen after toggling campaign unlocking to refresh its buttons.

| Key | Control | Effect |
| --- | --- | --- |
| F1 | Max Money | Raise hospital funds to at least **999,999,999** once. A larger balance is preserved. |
| F2 | Freeze Time of Day | Stop advancement of the hospital day/time while enabled. |
| F3 | Unlimited workers | Remove the staff hiring cap while enabled, including above 1,000 employees. Turning it off restores normal hiring rules and keeps hired staff. It does not spawn workers or change their skills/needs. |
| F4 | Unlock All Campaigns | Make installed campaigns selectable while enabled, without recording campaign completion or writing the profile. DLC ownership and installed content still apply. |
| F5 | Max Prestige | Keep the hospital's current calculated prestige at **100%** while enabled. |
| — | Patient patience | Set **1×–100×** waiting tolerance (suggested value **10×**, inactive until applied). Multiplies long-wait and long-visit departure limits and pharmacy warning/departure limits; wait-related satisfaction uses elapsed waiting time divided by the same multiplier. **1×** restores normal behavior. |
| F10 | Disable all | Restore patience to 1× and stop continuous effects. Money already granted is not reversed. |

The same controls are available in the English and Chinese HappyCheats interface. Actions require a fresh trainer session and an acknowledgement; disconnected, stale or unsupported sessions cannot receive commands. All controls remain clickable; hospital-only actions report an actionable error at the main menu. Trainer effects reset when the loaded hospital changes or closes. Current clients retain requested toggle states and restore them once the new hospital is ready; explicit Off or Disable all cancels that restoration. Numeric patience returns to 1× on a hospital change and must be applied again. A new client process starts with no remembered requests. A native unlimited-staff setting is preserved. Freezing the day clock leaves the rest of the hospital simulation running, while clock-based shift/day/hour events wait.

Money and staff hired afterward can persist when the game saves. Time-based systems wait while the day clock is frozen. Campaign unlocking is a runtime override, not a saved completion unlock. Turning off prestige stops its ongoing override; past day statistics or progression already saved by the game are not rolled back.

The installer verifies every owned file, preserves conflicting or modified mod files, and keeps a receipt for removal. **Remove trainer** removes the owned HappyCheats plugin while preserving the shared BepInEx loader for other mods. A conflicting loader must be resolved before installation; it is not silently overwritten.

## Validation and limitations

Bindings were inspected read-only against the actual installed assembly:

`1120D899FB74D16F31115C5C27325EDBD1154B68E9419E6B7EA03CC3D43B6F24`

Offline validation covers payload compilation, game binding inspection, isolated trainer behavior, command/session gates, installation ownership and integrity, and bilingual desktop/handheld interface checks. Package 1.0.1 is distributed as a signed independent GitHub package and identical LAN ZIP. The client and standalone EXE remain unchanged. Release evidence is retained under `artifacts/project-hospital/`.

On 31 October 2026, an authorized JACK-PC test reproduced the startup failure at the main menu: the stripped game libraries lack `System.IO.InvalidDataException`. Trainer 0.1.1 uses `IOException`, and a new framework-reference check rejects the previous payload against the actual game libraries. The corrected trainer produced a valid session, retained a fresh heartbeat while backgrounded, and acknowledged a queued **Disable all** after returning to the main menu. No hospital was loaded. Actual hospital gameplay, save-affecting controls, large-hospital performance and another PC remain unverified.

After downloading this cheat update, close the game and apply trainer 0.1.2 using the Library update flow described above. A paused game can wait to execute queued commands until you return to it; its connection heartbeat remains alive. The background worker only republishes immutable status snapshots and never touches game objects or executes cheats.

The client reports missing trainer files, missing current-launch loader logs, startup exceptions and version mismatches. Click-time errors are recorded in `%LOCALAPPDATA%/HappyCheats/client-errors.log`; loader details are in `BepInEx/LogOutput.log` under the selected game folder. Shared-menu buttons keep foreground focus with the game from 0.3.63; explicit numeric text editing still takes keyboard focus. Use windowed or borderless play.

Build with PowerShell 7:

```powershell
.\scripts\Build-ProjectHospital.ps1
.\scripts\Test-ProjectHospital.ps1
# For another installation:
.\scripts\Build.ps1 -ProjectHospitalGameDirectory 'D:\SteamLibrary\steamapps\common\Project Hospital' -Zip -SingleExe
```

The trainer targets the game's legacy .NET 3.5/Unity runtime. Game assemblies are used locally for inspection and compilation and are excluded from distribution.


Package 1.0.1 was checked on 4 November 2026 by read-only inspection of the supported assembly, payload compilation, isolated worker/patience/protocol tests and shared UI/menu/controller regression checks. No game was launched and no save was changed for this update. Runtime hook loading, actual worker hiring and patient gameplay effects remain unverified. Closing time, medical deterioration, collapse, death, needs and non-wait satisfaction effects continue to follow the game. A higher patience setting does not make patients immortal or guarantee they stay after closing. Changing back to 1× immediately restores the original thresholds; existing elapsed waiting time is retained, so already overdue patients may leave. The native statistics panel can still display the game's configured staff cap; the active hiring check and trainer status show the override.

新增功能：**无限员工**取消招聘人数上限（不会自动生成员工）；**病人耐心**可设置为 **1～100倍**，建议10倍，1倍恢复正常。倍率延长候诊、整体就诊和药房等待的容忍时长，并延后等待造成的满意度影响。关门、病情恶化、倒地、死亡和其他需求仍按游戏规则运行。关闭全部功能或切换医院后恢复1倍。原有等待时间不会清零，恢复正常后已超时的病人可能离开。使用游戏库排序旁的“更新”获取本游戏软件包，关闭游戏后应用；旧客户端还需重新加载修改器库并点击“安装 / 更新”。此次没有启动游戏或修改存档，实际招聘与候诊效果尚未进行游戏内验证。
