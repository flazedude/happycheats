# Source synchronization between JACK-PC and GPD-PC

Both PCs receive and send source through [flazedude/happycheats](https://github.com/flazedude/happycheats). Run the commands below in PowerShell 7 on the PC where you are working. GitHub holds the shared history, so the other PC can catch up when it is online. Each PC keeps its own Git credentials, build prerequisites and checkout path.

## Get the helper

For a new checkout, choose an empty destination and clone:

```powershell
git clone https://github.com/flazedude/happycheats.git
Set-Location happycheats
```

For an older checkout without `scripts/Sync-Source.ps1`, first read `AGENTS.md` and run `git status --short --branch`. Only when you are on a clean `main` branch, bootstrap with:

```powershell
git fetch --prune origin
git pull --ff-only origin main
```

If there are unfinished edits, a different current branch or diverged commits, preserve them and review the incoming history before updating. Re-read `AGENTS.md` after receiving changes.

## Receive and send work

At the start of a task, inspect the current branch and receive its published changes:

```powershell
.\scripts\Sync-Source.ps1 -Mode Status
.\scripts\Sync-Source.ps1
```

The default `Pull` mode fetches and fast-forwards the **current branch only**. `Status` fetches and reports its relationship to GitHub without changing working files. To operate on a different checkout without changing your shell directory, add `-RepositoryPath 'C:\path\to\happycheats'`.

Start each change on its own `codex/` branch after updating `main`; include the PC in its name. For example, from a clean, updated `main` checkout on GPD-PC:

```powershell
git switch --no-track -c codex/example-change-gpd main
```

Use a separate worktree when another chat is using the same checkout. After finishing and checking the change, review the diff, stage only its intended files and commit them. Then send the committed branch:

```powershell
.\scripts\Sync-Source.ps1 -Mode Push
```

`Push` fetches first, requires a clean checkout and sends the current branch with a normal Git push. It can publish a new task branch, establishes its same-name `origin` upstream and verifies the remote commit. It does not integrate that task into `main`. Integrate completed, reviewed changes deliberately, rerun affected checks and push `main` before publishing a client release.

To continue a published task branch on the other PC, first preserve any work in that checkout, fetch, and deliberately select the branch. For a branch that has no local copy yet:

```powershell
git fetch --prune origin
git switch --track origin/codex/example-change-gpd
.\scripts\Sync-Source.ps1
```

If the branch already exists locally, use `git switch codex/example-change-gpd`, then run the helper. Work completed on JACK-PC follows the same steps in the opposite direction.

## Safeguards and scope

The helper accepts the official GitHub `origin` only and requires any configured upstream to be the same branch name on `origin`. Pull and Push require clean source and stop for divergence or an unfinished Git operation; detached HEAD and mismatched upstreams also require deliberate repair. It never commits, stages, stashes, merges divergent history, rebases, resets, force-pushes or switches branches. A failed fetch or rejected push is reported, not treated as successful synchronization. Review and resolve conflicts while preserving both PCs' work, then retry.

Only committed source travels through Git. Ignored build output, trainer caches, local LAN configuration, credentials and private signing keys stay local. Updating the installed client and refreshing a LAN mirror are separate operations; see [GitHub publishing](github-updates.md) and [LAN mirrors](lan-updates.md). No background source-sync task is installed. Documentation or source-sync tooling changes require a source commit and push but do not by themselves require a new client binary release. A successful push makes the work available to the peer; its checkout is current only after its own successful pull.
