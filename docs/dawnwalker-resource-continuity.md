# Resource lock continuity — 3 November 2026

Dawnwalker package **1.0.6**, payload **0.5.5**, repairs a captured native
effect-retirement case on Steam build **26379537 / CL-265636**. Both inspected
executable hashes remain supported. HappyCheats **0.3.69** separately adds
shared toggle continuity for all current and future module metadata.

## Captured failure

The user reported that enabled Infinite health/stamina still allowed their
bars to drain, and that controls switched off during loading/location changes.
The user played the already-running game and enabled F1/F2 while a read-only
observer sampled resource values, component handles, active effects and the
trainer heartbeat. No agent game command, input, memory write, launch, close
or save change was used for this capture.

At **19:12:00 China time**, native health handle **1103** and stamina handle
**1105** each identified one active override effect; both trainer flags were
true. At **19:12:07.683**, both effects were absent but the component still
stored the same IDs. At **19:12:07.788**, both trainer flags were false while
the last acknowledgement still claimed the stamina lock was enabled. This
establishes a silent cancellation after native effects end, not which game
routine removed them. Other passive samples measured stamina below its
575 maximum while continuous blood refill was enabled. Invulnerability was
also enabled, so the capture does not isolate ordinary health damage.

The previous resource refresh treated every expired lease as an Off request.
It released its bookkeeping and cleared the flag. The next regular status
refresh also hid the transient diagnostic behind the ordinary Ready message.

## Repair

Within the same validated character/component/ASC session, an effect with a
native-confirmed zero stack count retires only its lease. The requested
toggle remains on. A new lock is acquired through the existing guarded native
path only when the current component handle is also confirmed inactive.
An active game-owned replacement is preserved without adoption or unlock;
the request waits until it ends. Invalid/unreadable or uncertain results
fail closed and retain a diagnostic rather than authorizing another effect.
Explicit disable, death, invalid ownership and session cleanup retain their
existing native safety checks. Repeated renewals never accumulate active
trainer locks. Renewal runs on the existing 250 ms maintenance cycle; an
effect retirement can therefore leave a gap until the next validated poll.
This is not a guarantee against scripted death or an instantly lethal hit
during such a gap.

F4 / Menu held 350 ms + D-pad Left now means **Refill blood and stamina
continuously**. It keeps the existing `dawnwalker_blood` action ID for old
controller clients. Full blood no longer skips stamina maintenance. Both
bindings must be available; player ownership is checked again between the
two resource operations. Stamina uses the existing verified owned/temporary
lock refill path. Combined refill does not enable the separate Infinite
stamina toggle, and turning it off does not cancel a separately enabled
Infinite stamina request. Full resources avoid unnecessary writes. Blood
segment restoration and any normally saved refills retain their prior scope.

## Shared client behavior

Client 0.3.69 keeps requested toggles separately from confirmed engine state.
Loading/disconnection/session replacement retains On requests and displays
**On · waiting to restore**. Once the module reports a connected, ready,
available session, the client restores each pending toggle through its
existing explicit-enabled command path. This uses metadata, including unknown
future modules, rather than a game-specific renderer or controller profile.
Off and Disable all cancel pending intent even if the native cleanup command
cannot yet run. Stable-session native hotkey Off remains authoritative.
One-shot actions, numeric grants and manual teleport are never replayed.
Intent is scoped to each game and the current client process; a new client
process starts with no remembered requests. Elden Ring Connect remains
explicit. Failed restore attempts are reported/logged and are not retried
against an unchanged state; Off then On retries deliberately.

## Validation limits

The eight Dawnwalker Lua suites pass **7,995 assertions/checks**, including
actual binding/resource-extension integration, expired effects, foreign
replacements, explicit cancellation, invalid state, bounded repeated renewal,
both refill resources, partial failure and session change between writes.
The packaged runtime rejects 15 unsafe hook profiles. Shared continuity
checks exercise 17 catalog games and an unknown future-game fixture, with
1,414 assertions plus actual main-window module dispatch/cancellation/failure
checks. Complete publication also runs the normal offline release suites.

The new payload has not been installed into the running game. Actual effect
renewal, resource depletion, loading/location restoration, physical-controller
behavior and performance with this release still need gameplay verification.
No such verification is inferred from the captured old-payload failure or
the synthetic regressions. Close the game before installing the signed update.
