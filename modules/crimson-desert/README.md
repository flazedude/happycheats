# Crimson Desert trainer and development tooling

Trainer **0.2.8** supports Steam build **26329202 / PE3446**, with remapped hooks, metadata and both Normal/ChildOnly client vtables. It preserves the Kliff class correction from 0.2.7 and the local-self, possession, vital and movement checks; foreign classes remain rejected. Teleport and speed report specific unavailable reasons. Current validation is static and synthetic only; actual gameplay, travel and landing remain unverified. See [PE3446 evidence](../../docs/crimson-desert-3446.md) and the historical [Kliff inspection](../../docs/crimson-desert-3424.md).

Trainer **0.2.5** avoids republishing unchanged complete player identities during periodic refresh. This closes a possible missed-refill interleaving in the actual stat hook while retaining invalidation on real transitions and strict native identity/lease checks. The regression fixture reproduces the old gap; it does not establish the cause of the reported death or live gameplay success. The allocation correction below remains included.

The native runtime in `native/` implements health, stamina, spirit, outgoing player damage, guarded horizontal movement speed, manual marker teleport and automatic teleport on new world-map placements for the pinned Steam executable. Trainer 0.2.1 uses a verified UI sender callsite, bounded event queue, player locomotion callback and native transform synchronization. Pins without height preserve current world elevation; terrain landing is not validated. Build it with `scripts/Build-CrimsonDesert.ps1`. The client module and explicit attachment/owned installation live in `src/HappyCheats.Core/CrimsonDesert*.cs`. See [setup, evidence and limitations](../../docs/crimson-desert.md). Item usage is unavailable; all implemented controls still need gameplay validation.

Trainer **0.2.4** corrects the 1 GiB allocator search limit behind the observed damage and marker hook failures. It preserves the original near-first search, then reuses or allocates a block within a conservative x64 jump range, rejecting overflowing RIP-relative or relay displacements. Read-only live inspection confirmed a reachable existing block outside the old limit; the new DLL's hook installation and gameplay remain unverified. The exact hook diagnostics introduced in 0.2.2 now scan the full fallback range with the same time and region limits. `scripts/Test-CrimsonDesert.ps1` tests the exact prologues, allocation fallback/reuse, exhaustion, relocation safety, diagnostics and teleport paths in disposable fixtures without opening the game. Process mitigations remain unchanged.

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
