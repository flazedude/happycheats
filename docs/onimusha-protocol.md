# Onimusha launcher protocol

The Onimusha bridge supports the shipped Lua runtime **0.4.0**, Steam app **2638890**, and tested Steam build **24769601**. It exposes four combat controls. Onimusha teleport is not part of this protocol; old `teleport_to_pin` requests are rejected as unknown controls.

## Transport and readiness

Paths are relative to the selected game's installation directory:

- Runtime status: `reframework/data/onimusha_trainer_status.json`
- Launcher command mailbox: `reframework/data/onimusha_trainer_command.json`

The launcher verifies the selected executable's running process and accepts a status file only when it is newer than the process start and no more than ten seconds old. A valid heartbeat has schema version 1, runtime version `0.4.0`, and a nonempty `session_id`. Controls require `player.ready` to be the JSON boolean `true`.

Status values display the player's health and red bar. The four toggle states come from `enabled`. A combat command requires its corresponding `features` binding to be installed and free of faults.

## Commands

Each command contains the current `session_id` and a new `request_id`. The launcher serializes requests and rechecks gameplay readiness and the session before writing the mailbox.

| Key | Control ID | Command field |
| --- | --- | --- |
| F1 | `unlimited_health` | `"enabled": { "unlimited_health": true }` |
| F2 | `auto_parry` | `"enabled": { "auto_parry": true }` |
| F3 | `one_hit_stun` | `"enabled": { "one_hit_stun": true }` |
| F4 | `stun_immunity` | `"enabled": { "stun_immunity": true }` |
| F5 | `disable_all` | `"disable_all": true` |
| F6 | `hud` | `"hud_visible": true` |

Use `false` to disable a toggle or hide the trainer HUD. The F1–F6 keys are also handled by the in-game runtime. The HUD and disable-all commands do not require any individual combat binding to be available.

For example, enabling unlimited health writes:

```json
{
  "session_id": "current-runtime-session",
  "request_id": "unique-request-id",
  "enabled": { "unlimited_health": true }
}
```

## Acknowledgements

A same-session status acknowledges a request when `last_command` equals its request ID. No action-result extension is required. Previous requests and temporary gaps while the status file is being replaced do not acknowledge the pending command. A different active session rejects it.

The launcher waits up to eight seconds for acknowledgement. It removes an unconsumed mailbox command on completion or timeout so that a timed-out command cannot apply later from that file. This cannot undo an operation the runtime has already consumed.

## Startup menu preference

After installation and before launching through Steam, the launcher prepares REFramework's closed-menu startup preference when Onimusha is not running:

```text
REFrameworkConfig_MenuOpen=false
REFrameworkConfig_RememberMenuState=true
```

These settings are stored in `re2_fw_config.txt`. Other settings, including the menu hotkey, are preserved. This startup preference does not hide the separate Lua trainer HUD, and writing a preference file does not close a menu in a running game.
