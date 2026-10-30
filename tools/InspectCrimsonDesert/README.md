# Crimson Desert offline inspection

For the current PE3424 / Steam build 26317560 profile, use the source-guard
verifier below and the [current evidence](../../docs/crimson-desert-3424.md).
The PE2976 findings retained here are historical, with their original addresses.

`inspect_binary.py` opens the game executable read-only, parses PE section headers,
hashes its bytes, and scans named byte patterns from a supplied C++ header. It
does not open a game process or modify game files. Wildcards are `?` or `??`.
Character-manager anchors additionally report their RIP-relative target.

Example (use the available Python runtime):

```powershell
python .\tools\InspectCrimsonDesert\inspect_binary.py `
  'F:\Games\steamapps\steamapps\common\Crimson Desert\bin64\CrimsonDesert.exe' `
  --source .\artifacts\crimson-research\gugi97\offsets.h `
  --output .\artifacts\crimson-research\installed-binary-inspection.json
```

The research header is a separately downloaded upstream artifact, not a required
runtime dependency. Match counts include the full disk image; counts are not
proof of feature behavior in a running game. Results retain at most the first
256 locations for very broad patterns, while `match_count` records the total.

## Historical installation, 26 September 2026 (PE2976)

- Steam application 3321460, build 25477059.
- File/product version 1.0.0.2976; 384,521,624 bytes.
- SHA-256: `57da440d72f4db974f25fef047cf84c4dadd999a88cb2a3c5af4c9bd67fde1e7`.
- AMD64, preferred image base `0x140000000`, PE timestamp `1790086912`,
  SizeOfImage `389722112` (`0x173AB000`).
- The image has executable code in renamed `.rsrc` and `.xtls` sections. Several
  target routines are statically readable; parts of the image are obfuscated.
  Do not infer normal section purposes from section names.

All addresses below are RVAs, not file offsets or runtime absolute addresses.
Patterns from the MIT-licensed gugi97 source each matched once for StatCommit
(`0xC7EFDE0`), DamageApply (`0x17AE080`), MoveUpdate (`0x42820E0`), and LocoStepper
(`0x369FFC0`). Four separate client character-manager anchors independently
resolved the same global, `0x6D696B8`. Earlier client/server labels for this global were incorrect: read-only live inspection on 27 September found server-side actors there, and `0x6D69698` resolved to the same manager. The current client character used by world-map code comes from global `0x6D69190` → `+0x30` service → `+0x50` owner.

Disassembly of StatCommit confirms the Win64 arguments RCX=entry, RDX=opaque argument,
R8=target, R9W=flag, and fields base `+0x18`, normalized `+0x20`, current `+0x08`,
and cap `+0x30`. The first fifteen bytes end on an instruction boundary and have
no RIP-relative instructions. DamageApply's first fifteen bytes are three
five-byte register home stores, also with no RIP-relative instructions.

Current code independently confirms the character type descriptor at owner
`+0x88`, with type byte at descriptor `+1` (for example, RVA `0x596358`). The
possessor round-trip is present at RVAs `0x28FD78A` and `0x2A21F4F`: read owner
`+0xA0`, then compare its `+0xD0` pointer with the same owner.

## Movement ownership evidence

Three independent current callsites establish this pointer chain:

```text
controlled client character owner
  -> [owner + 0x68]  actor
  -> [actor + 0x40]  physics object
  -> [physics + 0x140] locomotion component
  -> [component + 0x2C0] movement proxy
```

At RVA `0x596358`, the caller type-checks the character in RBX by its descriptor
at `+0x88`. At `0x596497`, it reads that same RBX through `+0x68`, then `+0x40`,
then `+0x140` and calls `0x369F060`. Independent callsites at `0x11853D6` and
`0x119232C` repeat the chain. In `0x369F060`, RDI preserves incoming RCX and is
passed unchanged as RCX to LocoStepper at `0x369F1D3`. LocoStepper reads its
movement proxy at `+0x2C0` and invokes MoveUpdate with that proxy.

Read-only live inspection confirmed this chain for the current client owner;
it is invalid for the server-side combat owner. This supports matching the
LocoStepper's component argument against a freshly resolved, controlled
client player's chain. Assigning the last observed MoveUpdate
argument as "the player" does not establish identity. The old upstream comment
giving proxy offset `+0x298` is stale for this installation.

LocoStepper uses RCX=component, XMM1=float delta time, R8=float velocity[3],
R9B=flag, with further arguments on the stack. Horizontal speed scaling should
leave velocity Y unchanged and must not assume a scratch argument is above a
large heap-pointer floor.

## Unresolved checks

The legacy item-decrement signature (including its exact instruction variant)
has zero matches in the installed image. The legacy marker-copy signature has
five matches and the legacy entity signature has zero. None is a safe current
binding solely on those patterns. Static checks do not validate actual gameplay,
map-marker teleport safety, saves, or network behavior. The game was not started.

## Verify the trainer's current bindings

`verify_bindings.py` reads the native source guards directly, then compares the
executable's PE machine, timestamp, image size, SHA-256 and every `Matches` byte
guard in its binding validators. It does not require an upstream research header.

```powershell
python .\tools\InspectCrimsonDesert\verify_bindings.py `
  'D:\Games\Steam\steamapps\common\Crimson Desert\bin64\CrimsonDesert.exe' `
  --output .\artifacts\crimson-research\binding-verification.json
python -m unittest discover -s .\tools\InspectCrimsonDesert -p test_verify_bindings.py
```

The source root defaults to this checkout; `--source-root <checkout>` compares
another checkout's guards. JSON is always printed, and `--output` optionally saves
it. Exit codes are **0** for all matches, **1** for mismatches or unmappable guarded
RVAs, and **2** for malformed files, unsupported source syntax or other inspection
errors. A guarded range must fit exactly one raw-backed section. Unparsed guards,
ambiguous mappings and files changed during inspection cannot pass.

This is read-only inspection of the game file, not signature discovery or proof
of field semantics, runtime hook installation, DLC behavior or gameplay. The
report records source hashes so the checked guards can be identified later.
