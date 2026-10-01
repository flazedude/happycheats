# The Witcher 3: Wild Hunt — Remastered

Original WitcherScript trainer for Steam app **292030**, installed build
**25575366**, `bin/x64_dx12/witcher3.exe` version **5.0.0.1041720**.
This installed Remastered build has no DX11 executable. Classic 1.32 and earlier
next-gen 4.x builds are not covered by the executable pin.

The payload provides health, stamina, adrenaline and potion-toxicity refills;
one-shot health/stamina restoration; 1,000 crowns; 1,000 XP; and one skill point.
Refills cannot prevent a lethal hit between script ticks. Currency and progression
changes can be persisted by normal game saves. The trainer does not open or edit
save files directly.

## Integration and validation boundary

Installation generates the minimal `r4Game.ws` override from the user's own
verified game source and injects an unsaved trainer state object into the game
tick. Vanilla game scripts are not distributed in this payload. The module owns
its mod and bridge files and must refuse conflicting overrides instead of merging
unknown mods automatically. Game startup performs actual WitcherScript
compilation; the development task did not start the game.

`scripts/Build-Witcher3.ps1` packages the original script and its checksum.
`scripts/Test-Witcher3.ps1` checks source/payload equality, lexical delimiters,
API names and call arities, control IDs, and protocol source guards. If this Steam
game is installed, it compares the executable pin and every used API declaration
read-only against the installed scripts. These checks are **not a WitcherScript
compiler, interpreter, or gameplay test**. No standalone WitcherScript compiler
was available in the game installation during development.

The runtime scopes commands to the current player session and accepts only a
bounded age, schema, and fixed action list. A 32-entry request history suppresses
replay. Commands are polled at most four times per second; their three-second
validity is shorter than the history window. Refills expire when the client stops
renewing its six-second lease. Runtime state is not declared `saved`.

## API evidence

The installed game ships plain source under `content/content0/scripts`. Relevant
declarations and real uses were inspected in:

- `game/actor.ws`: `IsAlive`, `ForceSetStat`, `GetStat`, `GetStatMax`.
- `game/player/r4Player.ws`: public `DrainToxicity` forwards to the ability manager.
- `game/gameplay/ability/abilityManager.ws`: toxicity reduction and change notification.
- `game/player/player.ws`: `GetMoney`, `AddMoney`.
- `game/player/playerWitcher.ws`: `AddPoints`, `GetWitcherPlayer`, public level manager.
- `game/temp.ws`: native `addmoney` and `addSkillPoints` console command examples.
- `game/gameplay/leveling/levelManager.ws`: a plain script class containing state,
  matching the trainer's state-object construction pattern.
- `core/2DArray.ws`, `core/string.ws`, `core/math.ws`, `core/engineTime.ws`,
  `engine/game.ws`, `game/inGameConfigWrapper.ws`: CSV, conversion, timing and INI APIs.

CD PROJEKT RED also documents the language, global objects and timer/exec
functions in its [WitcherScript language guide](https://cdprojektred.atlassian.net/wiki/spaces/W3REDkit/pages/36307090/WS+Language+Guide).
The publisher's [script reference](https://github.com/CDPR-Modding-Documentation/Witcher3-Scripts/blob/main/game/inGameConfigWrapper.ws)
contains the INI declaration. The installed 5.0 scripts, rather than the older web
copy, are the compatibility source for this module.

## Native I/O investigation

Read-only PE disassembly was performed against the installed executable with
SHA-256 `C272B2C2E61F84C758E28FAB69AB2915944DD1E539DBB435FAE9FC67494C7E25`.
Addresses below are preferred-base virtual addresses (`0x140000000`), evidence
for this one binary, and are **not** memory-write or hook targets used by the mod.

- `LoadCSVDirect` is registered to `0x1426F3B70`; it calls fresh CSV loading at
  `0x1426F5840`. This resolves the file via the engine filesystem, opens a reader,
  reads its contents, creates a new `C2dArray`, parses it, and disposes the reader.
  It does not retrieve a previously parsed resource from the CSV resource cache.
- The filesystem path walker at `0x14270EF00` splits a normalized path into child
  directories. A Windows drive-qualified path would be interpreted as a child
  name. Direct loading uses the physical directory lookup, bypassing normal mod
  resource lookup. In the default packaged game, startup at `0x141B71918` enables
  the flag that selects `content\\content0` at `0x141E70BA8`; initialization at
  `0x141E71CB1` uses the resulting install-relative directory as the root.
  Trainer 0.1.1 therefore keeps the reader path `happycheats_witcher3/`, but puts
  the two CSV files in `content/content0/happycheats_witcher3/`. The former mod
  content location was incorrect. Both CSV files must exist before game startup
  because directory membership is cached. Developer `-nosplitcook`/loose-file
  startup modes are outside this packaged Steam configuration.
- The loose-file reader at `0x1426E7420` rebuilds the physical filename and opens
  a new stream through `0x1426B8D60` on every call. The new stream is read in full;
  no prior stream or prior CSV size is reused by this call path.
- CSV parsing at `0x1426F5F30` stores the first row in a separate header array and
  subsequent rows in the data array. A header and one data row therefore match
  the runtime's `GetNumRows() == 1` contract.
- `WriteIniFile` is registered to `0x141F57120`. It appends `config\\base\\` to
  the game's bin directory and calls `0x1426FBF50`, which formats `%s%s.%s` with
  directory, supplied basename and `ini`. Thus the script passes
  `modsHappyCheatsWitcher3` **without** an extension. The first four basename
  characters `mods` select the `MODS` section (otherwise the writer uses `DLC`).
  It updates the requested key, marks the INI dirty and flushes through
  `0x1426FC7E0`. The expected output is
  `bin/config/base/modsHappyCheatsWitcher3.ini`, section `[MODS]`, key `Snapshot`.

The original trainer produced a real ready heartbeat in a user-started game,
confirming that script integration ran. Native inspection establishes the
corrected file path; command acknowledgement and gameplay with trainer 0.1.1
still require a user-started game session and are not verified.
