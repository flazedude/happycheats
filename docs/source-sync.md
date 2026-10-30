# Source synchronization between JACK-PC and GPD-PC

Both development PCs exchange source through [flazedude/happycheats on GitHub](https://github.com/flazedude/happycheats). `main` contains integrated changes; each task uses a separate `codex/` branch with the PC in its name. Either PC can publish committed source and receive the other PC's changes while the other computer is off.

Use PowerShell 7 and each PC's existing local GitHub authentication. Keep a separate local checkout on each PC. The helper below requires Git and does not require administrator access, a running LAN server, the signing key, or installed games.

## Receive source before starting work

In the existing checkout, run:

```powershell
.\scripts\Sync-Source.ps1
```

The helper accepts only the official `flazedude/happycheats` GitHub `origin`, including its configured push destination. It fetches `origin` and fast-forwards the current branch when possible. Source changes are limited to `main` and `codex/` branches: `main` receives `origin/main`; a `codex/` branch receives the same named remote branch. It never switches branches and refuses to modify a task branch that tracks `origin/main` or another mismatched upstream. A local branch with no matching remote branch can be published when its work is ready.

To fetch and inspect synchronization state without changing checked-out files:

```powershell
.\scripts\Sync-Source.ps1 -Status
```

Status also works with detached HEAD or a mismatched upstream so you can diagnose those states before selecting the intended branch or repairing its configuration. It does not make those states eligible for receive or publish operations.

To operate on another local checkout, add `-RepositoryPath 'C:\path\to\happycheats'`. Without this option, the script uses the checkout containing the script.

The alternative mode options are equivalent: `-Mode Pull` receives source (the default), `-Mode Push` publishes, and `-Mode Status` inspects. Use either the mode option or the corresponding `-Publish` / `-Status` switch.

The helper stops before updating files or publishing when there are uncommitted changes, untracked files, an unfinished Git operation, or divergent histories. Fast-forwarding also refuses to overwrite ignored local files with incoming tracked files. It does not commit, stash, reset, force-push, resolve conflicts, or run in the background. Preserve unfinished work and integrate deliberately before retrying. Fetching updates Git's remote-tracking information; it does not install a HappyCheats client update. Re-read `AGENTS.md` after receiving changes because the other PC may have updated shared instructions.

### First use in an older checkout

If the script is not present yet, inspect the checkout first:

```powershell
git status --short --branch
git branch --show-current
git remote -v
```

Only when the checkout is clean and the current branch is `main`, receive the helper with:

```powershell
git pull --ff-only origin main
.\scripts\Sync-Source.ps1 -Status
```

If this cannot fast-forward, keep the existing commits and resolve the branch history deliberately. Do not discard local files to make the command succeed. For a new checkout, clone the repository into a new local folder; preserve any existing checkout and machine configuration:

```powershell
git clone https://github.com/flazedude/happycheats.git happycheats
Set-Location happycheats
.\scripts\Sync-Source.ps1 -Status
```

## Send completed source to the other PC

Start a task from current `main`, choosing a distinct name such as `codex/source-fix-jack-pc` or `codex/source-fix-gpd-pc`. Explicitly avoid inheriting `main`'s upstream when creating the task branch:

```powershell
git switch --no-track -c codex/source-fix-jack-pc main
```

If another chat is using the checkout, create a separate worktree with its own branch and build output instead of switching that chat's branch. After fetching and inspecting `origin/main`, use a new local folder:

```powershell
git worktree add --no-track -b codex/source-fix-jack-pc ..\happycheats-source-fix-jack-pc origin/main
Set-Location ..\happycheats-source-fix-jack-pc
```

If an existing task branch already tracks `origin/main`, inspect its current branch and upstream first:

```powershell
git branch --show-current
git branch -vv
git config --get-regexp '^branch\..*\.(remote|merge)$'
```

Only after confirming that the current branch is the intended `codex/` task branch and the inherited upstream is incorrect, remove that upstream with `git branch --unset-upstream`. This preserves all commits and files. If the same named task branch already exists on GitHub, fetch and deliberately set `git branch --set-upstream-to=origin/codex/source-fix-jack-pc` instead, using its actual name. A successful first `-Publish` establishes the matching upstream automatically. Keep `main` tracking `origin/main`.

Review the diff, run the relevant checks, and stage only the intended files. After committing those files, run:

```powershell
.\scripts\Sync-Source.ps1 -Publish
```

This fetches first and pushes the current `main` or `codex/` branch to the same named branch on `origin` with a normal push. The push pins the inspected commit and verifies the published commit, so concurrent local work is not silently included. If the remote branch is ahead, receive its changes first, review them and rerun affected checks before publishing. It publishes existing commits; it does not include uncommitted edits or automatically integrate a task branch into `main`. A rejected push means another PC may have advanced that branch: fetch, inspect, integrate, rerun affected checks, and retry without force.

On the other PC, a clean checkout can receive a shared task branch as follows. Substitute the actual branch name:

```powershell
git fetch origin --prune
git switch --track origin/codex/source-fix-jack-pc
.\scripts\Sync-Source.ps1
```

If that branch already exists locally, use `git switch codex/source-fix-jack-pc` instead. Merge incoming commits into branches that have already been shared; rebase only unpublished local commits.

## Integrate completed changes into main

Finish review and relevant checks before integration. In a clean checkout dedicated to integration, substitute the completed task branch below:

```powershell
git fetch origin --prune
git switch main
git merge --ff-only origin/main
git merge --no-edit codex/source-fix-jack-pc
# Rerun checks affected by the integration before publishing.
.\scripts\Sync-Source.ps1 -Publish
```

If `main` and `origin/main` have diverged, inspect and merge the incoming changes deliberately instead of resetting or forcing the update. Resolve any conflicts to preserve both PCs' intended changes. If another PC advances `main` before the push, repeat the fetch/integration/check cycle. Verify the completed commit is present on `origin/main`. The other PC receives it by running the helper on its clean `main` checkout at the beginning of its next task.

## What stays local

Git exchanges tracked source, scripts, tests, instructions, and documentation. Build outputs (`dist`, `artifacts`, `payload`, `bin`, `obj`), private signing keys, credentials, and machine-specific settings stay outside source synchronization. In particular, preserve each PC's `%LOCALAPPDATA%\HappyCheatsPublisher\publisher.json`, `artifacts\lan-updates\server.json`, signing-key paths, LAN address, and startup task. `.gitignore` excludes the existing local outputs and sensitive configuration; review staged files before every commit.

Signed release distribution remains a separate workflow: [GitHub publishing](github-updates.md) and [LAN mirroring](lan-updates.md). Pulling source does not rebuild or install the app, enable a mirror task, or copy a peer's server configuration. Changes limited to this source-sync helper, its tests and documentation require source sync, not a new client binary. Client or payload changes still require the complete release workflow in `AGENTS.md`.

A successful push proves GitHub has the source. It does not prove the other PC has fetched it. Report the peer's last verified checkout state, and verify its local commit when working on that PC.
