# Crimson Desert trainer and development tooling

The native runtime in `native/` implements health, stamina, spirit, outgoing player damage, guarded horizontal movement speed, manual marker teleport and automatic teleport on new world-map placements for the pinned Steam executable. Trainer 0.2.1 uses a verified UI sender callsite, bounded event queue, player locomotion callback and native transform synchronization. Pins without height preserve current world elevation; terrain landing is not validated. Build it with `scripts/Build-CrimsonDesert.ps1`. The client module and explicit attachment/owned installation live in `src/HappyCheats.Core/CrimsonDesert*.cs`. See [setup, evidence and limitations](../../docs/crimson-desert.md). Item usage is unavailable; all implemented controls still need gameplay validation.

Trainer **0.2.2** adds exact hook creation/enabling diagnostics and bounded read-only failure snapshots. The reported game hook failure remains unconfirmed; no allocator or process-mitigation workaround is included. `scripts/Test-CrimsonDesert.ps1` tests the exact hook prologues and diagnostic failure paths in disposable fixtures without opening the game.

`HappyCheats.CrimsonDesert.Engine` provides dependency-free, read-only AMD64 PE32+ inspection and byte-pattern scanning. It never opens a game process or writes process memory. This library alone is not an operational cheat.

- `BinaryPattern.Parse` accepts whitespace-separated hexadecimal bytes and whole-byte `?` / `??` wildcards. `FindAll` returns every complete match, including overlaps.
- `PeImage` copies and validates its input. `ReadRva` returns a separate copy and rejects virtual zero-fill, gaps, padding and reads crossing a section boundary.
- `FindExecutableMatches` scans raw-backed executable sections independently. `FindUniqueExecutableMatch` rejects missing or ambiguous signatures. `VirtualAddress` is based on the file's preferred image base; it is not an observed live-process address.
- This separate Engine library does not attach to a process. The native DLL and client integration above supply trainer behavior.

Run the isolated fixture suite with PowerShell 7:

```powershell
dotnet run --project modules/crimson-desert/tests/HappyCheats.CrimsonDesert.Tests -c Release --no-launch-profile
```

The synthetic fixtures cover wildcard matching, duplicate and absent signatures, section isolation, RVA/file-offset mapping, malformed PE headers, truncation, arithmetic boundaries and overlapping sections. They do not establish game compatibility or gameplay behavior.
