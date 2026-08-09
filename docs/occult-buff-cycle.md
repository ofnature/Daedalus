# Occult Crescent — phantom buff cycle

Scope for automating phantom-job self-buff collection. Written 2026-07-31 from a read of
BOCCHI (`.cursor/bocchi`, OhKannaDuh/BOCCHI) plus what Daedalus already owns.
**Revised 2026-08-08** — two field corrections from the user changed the shape of the feature
(see below); the original per-toon cycling design is superseded.

## What the feature is

Phantom job self-buffs last ~30 minutes and **persist after you switch away from the job**.
So you cycle: switch job → cast its buff → switch to the next → … → switch back to the job you
were playing. You end up carrying four or five permanent buffs on whatever job you actually run.

This is not a rotation feature. It runs out of combat and takes ~30-60 seconds. It is the single
highest-value bit of Occult automation that isn't already ours.

## ⚑ Field corrections (user, 2026-08-08) — these change the design

1. **The job change does NOT require a Knowledge Crystal.** `ChangeSupportJob` works anywhere in
   the zone. Unknown #4 below is resolved, and BOCCHI's "path to the nearest crystal first"
   behaviour is inherited from the *action's* requirement, not the job swap's.
2. **Casting a buff while near a crystal broadcasts it to the whole PARTY, zone-wide.** The
   crystal is not a gate on collecting the buff for yourself — it is a range amplifier. Refined
   2026-08-08: the recipients are **party members**, and they must be **in the zone**. Not
   everyone in the instance; not party members who are elsewhere.

   So the reach is: *in your party* **AND** *in this zone* — but at any distance within it. No
   need to gather at the crystal, no need to be near the buffer at all.

**Consequence: this stops being a per-toon cycle and becomes a single-toon broadcast.**

One character walks to a crystal, cycles the jobs, and every party member in the zone receives
each buff. The other toons do nothing: no job switching, no travel, no interruption to whatever
they are doing. For an 8-box fleet that is one ~60-second sequence instead of eight, and the
seven non-buffers never leave their farm spots.

The fleet already satisfies both conditions by construction — it plays as one party, and it has
to share an instance to play together at all. **But both are now real preconditions the feature
must check rather than assume**, and the failure is silent: a toon in a different instance, or
one that never got invited, simply receives nothing and no error says so.

That also demotes the Inquiring Mind A/B question below from "may collapse the design" to a
convenience question: if the per-job actions already broadcast party-wide from a crystal, one
cast per job is already the whole feature, and Inquiring Mind would only save job switches.

**Still open about the broadcast:**
- Does a party member who zones in **after** the cast miss that application entirely, and need
  the cycle re-run for them? Likely yes given it is a cast-time grant — which makes "buff after
  the fleet has assembled, not before" the correct ordering, and makes a late joiner a reason to
  re-run rather than wait for the refresh threshold.

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
- **Near a Knowledge Crystal — for the BROADCAST, not for the buff.** `ObjectKind.EventObj`,
  BaseId `2007457`, field-confirmed 2026-07-31 via the Draw Helper object labeller; same scan
  shape as the carrot filter in `WorldLineSelector`. Corrected 2026-08-08: the job change works
  anywhere, and a buff cast away from a crystal still lands **on the caster**. Crystal proximity
  is what turns it zone-wide.

  So proximity is a precondition of the *fleet-wide* mode, not of the feature. A solo run can
  cycle buffs anywhere; the crystal trip is what makes one toon's cycle cover everyone.

## Cast path (spiked 2026-08-08 — nothing new to build)

`ActionService.ExecuteOgcdRaw(ActionDefinition action, uint rawDispatchId, ulong targetId)` goes
straight to `ActionManager.UseAction(ActionType.Action, id, target)`. It has **no combat gate and
no `GetActionStatus` pre-check** — "Raw" exists precisely to bypass the latter — which is exactly
the fire-this-id-now path this feature needed. The scheduler is not involved.

The three pieces:

| Need | Existing API |
| --- | --- |
| Fire the action | `ActionService.ExecuteOgcdRaw(def, actionId, selfId)` |
| The `ActionDefinition` | `PhantomJobService.GetOrBuildDefinition(actionId, name)` — built from Lumina |
| Target for a self-buff | the player's own `GameObjectId` (what `PhantomActionLayer` passes for Occult Cure II) |

Gates available without the scheduler:
- `ActionService.CanExecuteActionId(id)` → `GetActionStatus(...) == 0`. **The game's own verdict**,
  covering level, learned, cooldown and duty-bar slotting in one call. Use this as the pre-cast
  gate rather than reimplementing the checks.
- `ActionService.IsActionReady(id)` → charges > 0 (cooldown only).
- `PhantomJobService.GetDutySlotIds()` → `DutyActionManager.GetDutyActionId(i)` for the slotted bar.

**Three caveats the implementation must respect:**

1. **`_blockedRepeatOgcdId` blocks re-firing the SAME oGCD within 1 second.** The cycle casts a
   different action per job so the happy path is unaffected, but a retry after a failed cast must
   wait more than a second or it will be silently swallowed.
2. **`ExecuteOgcdRaw` mutates rotation state** — `_ogcdsUsedThisCycle++`, `_history.RecordOgcd`,
   `RaiseActionExecuted`. Harmless out of combat, but it does pollute weave accounting and
   history. Worth accepting for the refusal logging it brings; worth knowing before someone
   debugs a phantom entry in the action history.
3. **The duty bar is the real gate.** Phantom actions are *duty* actions: the buff must be
   SLOTTED on that job's duty bar or the cast fails. `PhantomActionLayer.IsOnDutyBar` fails closed
   for this reason. After each `ChangeSupportJob`, re-read `GetDutySlotIds()` — a job whose buff
   is not slotted cannot be cycled, and the button should say so by name rather than reporting a
   generic failure.

## Partial buff sets — supported by design (user question, 2026-08-08)

**Yes, and it has to be.** A character will routinely not have all four, for four independent
reasons, and the cycle must skip and report rather than stall:

1. **Job not unlocked** — `JobLevels[job] == 0`. Daedalus already reads the per-job level array
   from `OccultCrescentState`.
2. **Job under-levelled for the buff.** Each action carries its own unlock level in
   `PhantomActions.cs`, and they line up exactly with the Inquiring Mind tooltip's requirements:
   Knight Pray Lv2, Bard Romeo's Ballad Lv2, Monk Counterstance **Lv3**, Dancer Quickstep Lv2.
3. **Action not slotted** on that job's duty bar (caveat 3 above).
4. **Toggled off** in config — the per-buff toggles already planned.

Treat 1–3 as "skip with a named reason", 4 as "skip silently, the user asked". The refresh policy
already handles subsets correctly: the minimum remaining is taken across the **enabled** buffs, so
a two-buff character refreshes on its own two and never waits on a buff it can never have.

The button's completion line is where this surfaces:
`Buffed 2 of 4 · Monk Lv1 (needs 3) · Dancer not unlocked`

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

## Unknowns

1. ~~**`ChangeSupportJob` in our pinned FFXIVClientStructs.**~~ **RESOLVED 2026-08-08 — it
   exists**, verified against the pinned commit `8121cbbc`:
   ```csharp
   [MemberFunction("E8 ?? ?? ?? ?? 48 8B 06 48 8B CE FF 50 ?? EB ?? 48 8B 06 48 8B CE C7 46")]
   public static partial bool ChangeSupportJob(byte id);
   ```
   Static, takes the support-job byte, and **returns bool** — so it reports success rather than
   failing silently, which is better than this plan originally assumed. The complementary read is
   `OccultCrescentState` `0x91` `CurrentSupportJob` (MKDSupportJob RowId) — capture it before the
   cycle, restore it after. It is absent from the ClientStructs XML docs only because it carries
   no doc comment; do not conclude from a doc grep that it is missing.
2. ~~**The casting path out of combat.**~~ **RESOLVED 2026-08-08 by spike — the path already
   exists, no new ActionService machinery needed.** See "Cast path" below.
3. **Inquiring Mind's action ID**, and whether it is worth having at all now that the per-job
   actions are known to broadcast from a crystal. Demoted from blocking to optional.
4. ~~**Whether crystal proximity is required for the job change.**~~ **RESOLVED 2026-08-08 —
   it is not.** See the field corrections at the top.
5. ~~**The broadcast's exact scope.**~~ **Largely RESOLVED 2026-08-08: party members, in the
   zone, at any distance.** Only one sub-question remains — whether a party member who zones in
   *after* the cast misses that application. Decides whether a late joiner triggers a re-run or
   waits for the refresh threshold; does not affect whether the feature works.

## Phases

1. Spike the remaining unknowns — chiefly the out-of-combat cast path (#2)
2. State machine + refresh policy + tests (no UI)
3. **The whole UI is one button on the Occult window** (user, 2026-08-08) — "Buff" / "Apply
   buffs", which kicks the cycle and reports progress. No panel, no per-buff UI beyond the
   config toggles. What it needs to carry:
   - **Disabled with a reason when it cannot run** — not in an Occult zone, in combat, no
     Knowledge Crystal nearby (fleet mode only), cycle already running. A greyed button that
     does not say why is the thing users file bugs about.
   - **Live state while running** — which job it is on, e.g. "Switching to Bard (2/4)". The
     cycle takes 30-60s of the character doing visibly odd things; silence during it reads as a
     hang.
   - **Coverage on completion** — "Buffed 4 of 4 · party 7/8, Prometheus not in the zone".
     This is where the silent-failure preconditions become visible.
   - The lowest remaining buff timer, so "do I need to re-run?" is answerable at a glance.
4. Automatic refresh when the threshold trips — **optional**, and deliberately after the button.
   A manual button is the honest version of a feature that switches your job four times; make it
   trustworthy before making it automatic.
5. **Fleet mode: designate ONE buffer.** With the party-wide broadcast, the other toons need no
   logic at all — they simply receive. The work is picking the buffer (config, or the toon
   nearest a crystal), routing it there, and suppressing the cycle on everyone else so eight
   boxes don't all run it. Cheaper than the original per-toon design and strictly better.

   **Verify the preconditions rather than assuming them**, because both fail silently:
   - Every intended recipient is in the **same party** as the buffer. The LAN roster already
     knows the fleet; the in-game party list is the one that actually counts here.
   - Every recipient is in the **same zone instance**. Occult Crescent runs multiple instances,
     and a toon in the wrong one is invisible to the broadcast while looking perfectly fine.
   - Report who was covered after a cycle — "buffed 7 of 8, Prometheus not in the zone" is the
     difference between a working fleet feature and one that quietly half-works.

   Ordering follows from the grant being cast-time: **buff after the fleet has assembled**, and
   treat a late arrival as a reason to re-run the cycle rather than wait for the refresh
   threshold.
6. Optional: audit the remaining phantom jobs for buffs BOCCHI missed

Phases 2-4 are the bulk. The per-toon cycling machinery the original plan sized for is no longer
needed — one toon's cycle covers the fleet.
