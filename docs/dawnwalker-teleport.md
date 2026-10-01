# Dawnwalker map-pin teleport investigation

Inspected 26 September 2026, Steam build **25232147**, Unreal Engine **5.5.4**.
Executable: `Dawnwalker/Binaries/Win64/Dawnwalker.exe`.
SHA-256: `CB9B7D7BD88A6754C0A9C08318AA64D5013DDFD92D5BADCAE84E1B4EA980DCFC`.

The reflected map API contains real `IsSetUserMappin`, `GetTrackedMappin`,
`GetMappinInstanceLocation`, and `SetUserMappin` functions. Their existence does
not establish a supported teleport path. A tracked pin must be checked as
`EMappinType::User` (23); an arbitrary tracked quest target is not a user pin.

`/Script/DogwoodMap.MappinSystemBlueprintLibrary.DebugTeleportPlayer` has a
native entry at **RVA `0x05D56CC0`**. Its reflected parameters are a
`MappinSystemImpl` object, `Vector2D` location (two doubles), and an output
`EMappinFunctionReturnType` value. Static disassembly of this exact executable
shows that the wrapper parses its arguments, then writes byte **1** to the
output at **RVA `0x05D56E83`** and returns at **RVA `0x05D56E92`**. It does not
call a teleport implementation or use the supplied location for movement.

Read-only inspection of the live reflected enum confirms:

| Enum member | Value |
| --- | --- |
| `EMappinFunctionReturnType::Handled` | 0 |
| `EMappinFunctionReturnType::NotHandled` | 1 |

Therefore this shipped debug entry is a **NotHandled stub**. HappyCheats does
not expose map-pin teleport for this build. A future implementation needs a
verified user-pin identity, map-to-world position conversion, and a movement
path that handles collision, ground height, and world streaming correctly.

Validation was limited to local executable disassembly, reflected metadata,
and read-only process inspection. No function was invoked in the game, no
teleport or gameplay behavior was tested, and no game files or saves were
modified by this investigation. The local discovery records are
`artifacts/dawnwalker/metadata.json` and `artifacts/dawnwalker/objects.json`.

## Further inspection for trainer 0.2.1

`FastTravelSystem.FastTravelToLocation` is a real native WorldSubsystem method
accepting `FVector` and `FRotator`, returning Failure (0), MissingMapPin (1), or
FastTravel (2). Its phased timer unloads the old area, loads the destination,
resolves a floor/capsule offset, teleports and finishes. The wrapper is at RVA
`0x5D56F70`, implementation `0x5D5BB10`, final placement `0x5D5D8BC`.

The remaining obstacle is the position contract: `SetUserMappin` at RVA
`0x5D5D408` stores the supplied world vector without height projection. The
final placement does not establish a sky-to-ground trace for arbitrary pin Z.
Saved reflection names identify the player's `TeleportToMarker` Blueprint and
the map widget's `Set User Mappin To Mouse Position` / `MapToWorldPosition`.
Their live bytecode is needed to verify how the map constructs the height.
The isolated read-only capture helper is prepared under
`artifacts/dawnwalker/capture_movement_readonly.py`; it was not run, since
the game was closed and the launch-permission question remained unanswered.
No guessed coordinates or temporary streaming actors are shipped.
