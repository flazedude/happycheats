# Read-only Dawnwalker inspection

These helpers inspect the locally installed native Unreal game. They never
launch the game, call game functions, allocate remote memory, write process
memory, or modify game files/saves. `OpenProcess` requests query/read rights only.

Requirements: 64-bit Python 3 and `pefile` (`py -m pip install pefile`). The
investigation also used `capstone` for read-only disassembly.

Run from the HappyCheats repository root:

```powershell
py tools/InspectDawnwalker/read_game.py
py tools/InspectDawnwalker/live.py
py tools/InspectDawnwalker/dump.py
py tools/InspectDawnwalker/metadata.py
```

The static helper defaults to the originally inspected installation path. Edit
`GAME` if the same binary is installed elsewhere. Live helpers find exactly one
already-running `Dawnwalker.exe`, resolve its ASLR base and refuse a binary whose
SHA-256 differs from the inspected build. Outputs go to `artifacts/dawnwalker`.
`dump.py` produces the object snapshot consumed by `metadata.py`; run them in
that order during the same process session. Snapshots are transient inspection
data, not shipped trainer payloads.

The name-pool/object-array RVAs and reflection layout in these scripts are
specific to build 25232147 / CL-258504. Production trainer code uses reflected
names, not these addresses. These helpers are not a universal Unreal dumper.
