# Occult Crescent — phantom buff cycle

Scope for automating phantom-job self-buff collection. Written 2026-07-31 from a read of
BOCCHI (`.cursor/bocchi`, OhKannaDuh/BOCCHI) plus what Daedalus already owns.

## What the feature is

Phantom job self-buffs last ~30 minutes and **persist after you switch away from the job**.
So you cycle: switch job → cast its buff → switch to the next → … → switch back to the job you
were playing. You end up carrying four or five permanent buffs on whatever job you actually run.

This is not a rotation feature. It runs out of combat, at a Knowledge Crystal, and takes ~30-60
seconds. It is the single highest-value bit of Occult automation that isn't already ours.

## The buffs

BOCCHI covers five. Action IDs are Daedalus's own (`Data/PhantomActions.cs`); status IDs are
lifted from BOCCHI's `Data/PlayerStatus.cs`.

Effects are from the in-game Inquiring Mind tooltip (user screenshot 2026-07-31), so they are
ground truth. All four last 30m.

| Job | Own action | Action ID | Buff | Status ID | What it does |
|---|---|---|---|---|---|
| Knight | Pray | 41589 | Enduring Fortitude | 4233 | −10% damage taken |
| Bard | Romeo's Ballad | 41609 | Romeo's Ballad | 4244 | +10% Phantom EXP from battle |
| Monk | Counterstance | 41597 | Fleetfooted | 4239 | +movement speed |
| Dancer | Quickstep | 46603 | Quicker Step | 4799 | +2% damage dealt |

Romeo's Ballad being a **Phantom EXP** buff, not a combat buff, matters: it is the one to keep up
while levelling phantom jobs and worthless once they're capped.

### ⚠ Inquiring Mind — unresolved, and it may collapse this whole design

The tooltip reads:

> When executed **near a knowledge crystal**, grants an effect to self **and nearby party
> members** based on your phantom job and its level.
> - Phantom Knight Effect (Level 2 or Higher): Grants Enduring Fortitude
> - Phantom Monk Effect (Level 3 or Higher): Grants Fleetfooted
> - Phantom Bard Effect (Level 2 or Higher): Grants Romeo's Ballad
> - Phantom Dancer Effect (Level 2 or Higher): Grants Quicker Step

Two readings, with very different consequences:

- **A — one cast grants everything you qualify for.** The four lines are evaluated together
  against your phantom job *levels*. Then the entire job-cycling design is unnecessary: stand at
  a crystal, press one button, done. Phases 2-4 below shrink to almost nothing.
- **B — the grant depends on the job you currently have equipped.** Then you still cycle jobs,
  but you cast Inquiring Mind on each instead of that job's own action, and the per-job actions
  above are an alternative route rather than the primary one.

The wording cuts both ways: "based on your phantom job and its level" is singular (favours B),
but four separate conditional grants would be pointless under B since only one line could ever
match at a time (favours A).

BOCCHI is no help — it models Inquiring Mind as simply granting Quicker Step, which matches
neither reading, and defaults it off. Its per-job chains cast each job's own action instead.

**The test:** at a crystal, on Phantom Knight Lv2+, cast Inquiring Mind and look at what lands.
All four buffs → A. Only Enduring Fortitude → B. Also check whether it is castable at all while a
non-Freelancer job is equipped — if it isn't, that alone settles it as A.

**Blocked for now:** the user's Phantom Freelancer is Lv.1 and its four actions unlock at Lv
5/10/15/20 (screenshot), so Inquiring Mind is not yet available to test.

Its action ID is also still uncaptured — it isn't in `PhantomActions.cs`, and BOCCHI can't supply
it because it fires phantom actions as `ActionType.GeneralAction` slot 32/33, pressing the hotbar
*slot* rather than the action. We cast by real ID (`PhantomActionLayer` already does this
successfully in combat), which is the better path but needs the real number.

### Party-wide grant — matters for the fleet

Inquiring Mind explicitly buffs "nearby party members". If reading A holds, one toon at a crystal
can buff the whole box fleet in a single cast, and this stops being a per-toon cycle entirely.
Worth confirming whether the per-job actions (Pray etc.) are self-only or also party-wide.

Worth auditing whether jobs BOCCHI ignores (Berserker, Ranger, Samurai, Geomancer, Time Mage,
Cannoneer, Chemist, Oracle, Thief, Mystic Knight, Gladiator, and the North Horn additions) also
have long self-buffs. BOCCHI's list may be incomplete rather than exhaustive — the same way its
zone support turned out to be.

## Mechanism

Job switching is a direct ClientStructs call, no menu automation:

```
PublicContentOccultCrescent.ChangeSupportJob(byteId)
```

`PhantomJobService` already reads `PublicContentOccultCrescent.GetState()` for job levels and
knowledge, so we own the read side; this is the write side.

BOCCHI's per-buff sequence, worth copying closely:

1. Skip if already on the target job
2. `ChangeSupportJob`, then **wait for the phantom-job status to appear** (don't assume)
3. Cast the buff (gate: recast ≤ 0)
4. Verify the status is present **and its remaining time is ≥ 1780s** — that is, confirm a fresh
   30-minute application rather than trusting the cast landed
5. 15s timeout per job, 60s for the whole cycle

Capture the starting job before step 1 and restore it at the end.

## Preconditions

- In an Occult zone (`PhantomJobData.NorthHornTerritoryId` / `SouthHornTerritoryId`)
- Out of combat
- **Near a Knowledge Crystal** — `ObjectKind.EventObj`, BaseId `2007457`, field-confirmed
  2026-07-31 via the Draw Helper object labeller. Same scan shape as the carrot filter in
  `WorldLineSelector`. The Inquiring Mind tooltip states the crystal requirement outright, so it
  is at minimum a requirement of *that action*. Whether the support-job change itself also needs
  crystal proximity is separate and still unconfirmed — BOCCHI gates its button on it and paths
  to the nearest crystal first, but that may just be inherited from the action's requirement.

## Refresh policy

Take the minimum remaining time across the *enabled* buffs, recomputed on a ~1s throttle. Refresh
when it drops below the threshold (BOCCHI: 10 minutes, range 0-25). A missing buff reads as 0 and
therefore also triggers.

## Proposed shape in Daedalus

`Services/Occult/PhantomBuffCycleService.cs` — an explicit state machine ticked from the existing
framework update. We have no chain/task library (BOCCHI leans on Ocelot's `Chain`), so this is
hand-rolled:

```
Idle → SwitchJob → AwaitJobStatus → Cast → AwaitBuff → (next buff) → RestoreJob → Idle
```

Every state carries a deadline; any timeout aborts the cycle and restores the starting job.

- Config on `PhantomConfig`: per-buff toggles, reapply threshold, master enable
- UI in `OccultWindow`: a manual "apply buffs" button (disabled unless preconditions hold) plus
  the live lowest-timer readout
- Tests: the state machine and the refresh policy are both pure and should be covered directly —
  transitions, timeouts, restore-on-abort, missing-buff-triggers-refresh, the
  Dancer/Freelancer mutual exclusion

## Unknowns to resolve first

These gate the estimate; all are cheap to check.

1. **`ChangeSupportJob` in our pinned FFXIVClientStructs.** BOCCHI calls it, but we pin
   `Dalamud.NET.Sdk/15.0.0` — confirm the method exists with that signature. If it doesn't, the
   only fallback is UI automation, which changes the shape of this entirely. **This is the one
   that can sink the feature.**
2. **The casting path out of combat.** `PhantomActionLayer` pushes through the rotation
   scheduler, which isn't running out of combat. `ActionService.ExecuteOgcd` /
   `ExecuteOgcdRaw` exist but want an `ActionDefinition` and scheduler context — likely needs a
   thin "fire this action id now" path that bypasses the GCD machinery.
3. **Inquiring Mind's action ID** (and whether it's worth having at all, given it duplicates
   Quickstep).
4. **Whether crystal proximity is genuinely required** for the job change, or only for the
   in-game UI. Now trivially testable — stand away from one and call it.

## Phases

1. Spike the four unknowns above
2. State machine + refresh policy + tests (no UI)
3. Config + OccultWindow button and readout — manual trigger only
4. Automatic refresh when the threshold trips
5. Optional: audit the remaining phantom jobs for buffs BOCCHI missed

Phases 2-4 are the bulk. Phase 1 should come back within a session.
