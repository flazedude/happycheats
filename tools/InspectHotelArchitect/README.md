# Read-only Hotel Architect inspection

`inspect_game.py` reads the installed PE file. It never launches a game, opens a
process, writes memory, modifies a save, or changes the installation. Python 3
with `pefile` is required; `capstone` is used only by `--disasm`.

From the repository root:

```powershell
py tools/InspectHotelArchitect/inspect_game.py --verify --fingerprint
```

The default path is the Steam installation inspected on this host. Override it
with `--exe <shipping-executable-path>`. Verification is deliberately pinned to
build 25340122. It checks the complete SHA-256, twenty generated property
descriptors and native code establishing the five custom fields and aging byte.
It fails for another build instead of assuming the offsets still apply.

Generated reports go to `artifacts/hotel-architect`; these are offline evidence,
not gameplay verification. See `docs/hotel-architect-bindings.md` for the
bindings, parameter semantics, and limits.

The remaining switches are investigation helpers: `--refs` finds exact ASCII
names and PE pointer references; `--pointers` finds references to an address;
`--props` decodes nearby generated property-pointer lists; `--qwords` displays a
small data region; `--disasm` disassembles an explicit file offset or preferred
VA. `--riprefs` recognizes a limited set of x64 RIP-relative instructions rather
than serving as a complete reference finder. `--dump` emits game-region strings.
