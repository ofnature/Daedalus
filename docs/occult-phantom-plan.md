# Occult Crescent (South Horn) Phantom Actions — Design Plan

> **Status: PLAN ONLY — no implementation until approved** (meld-optimizer precedent).
> Source references: `.cursor/rsr/RotationSolver.Basic/Rotations/Duties/DutyRotation.cs` + `PhantomRotation.cs`,
> `.cursor/rsr/RotationSolver/RebornRotations/Duty/PhantomDefault.cs`, `burn-reference/occult-crescent-phantom-jobs.md`.

---

## 1. How RSR does it (verified against source)

### Detection — no gauge struct, everything is player statuses
- **Territory gate:** `[DutyTerritory(1252)]` — South Horn's TerritoryType. RSR's `DataCenter.IsInOccultCrescentOp` is just a territory check.
- **Active phantom job + level:** each phantom job applies a **permanent player status whose stack count = phantom job level** (`StatusID.PhantomKnight`, `PhantomMonk`, `PhantomOracle`, …). RSR reads `PlayerStatusStack` and treats stack 255 as 0. `GetPhantomJob()` = first status with stacks > 0. Only ONE phantom job is active at a time, so this is unambiguous.
- **Per-action unlock:** every phantom action gates on `ActionCheck = () => KnightLevel >= N` (phantom level 1–6, from the status stacks). No normal job-level checks involved.
- **Duty-bar slots:** only up to 5 phantom actions are slotted on the duty action bar; an action must be **on a slot** to be usable (`Info.IsOnSlot` in RSR — reads the DutyActionManager). Same pattern RSR uses for Variant dungeon actions, including **multiple action-ID variants** of the same skill.

### Execution rules (PhantomDefault.cs)
- **Lockout guard:** `HasLockoutStatus` blocks ALL phantom actions while rotation-critical main-job statuses are up (e.g. MCH `Reassembled`, RDM `Manafication`/`Embolden`/`MagickedSwordplay`/`GrandImpactReady`, VPR buff-refresh windows). Phantom GCDs must never eat a buffed main-job GCD.
- **Burst hold:** config `SaveForBurstWindow` (default on) — damage phantom actions only fire inside the main job's burst window; survival/utility actions ignore it.
- **Category interleaving:** phantom actions are offered into RSR's existing ability lanes — interrupt (Mineuchi, Occult Falcon, Romeo's Ballad), dispel (Occult Dispel, Recuperation), defense (Phantom Guard, Defend, Shirahadori, Magic Shell…), heal (Occult Heal, Blessing/Judgment thresholds, Chemist items, Occult Chakra), general damage GCDs last.
- **Consumables:** Chemist/Samurai actions check inventory items — Occult Potion 47741, Occult Elixir 47743, Zeninage gil-toss 47740.
- **Special state machines:**
  - **Oracle:** `Predict` opens a 4-card deck (Cleansing / Starfall / Phantom Judgment / Blessing, tracked via `PredictionOf*` statuses). RSR tracks remaining cards across the window, prefers Starfall (huge damage, self-KO risk) with an **Invulnerability combo** (`SaveInvulnForStarfall`), falls back to Cleansing when tanking, plays the last card unconditionally.
  - **Cannoneer:** Dark vs Shock Cannon depend on target blind/paralysis susceptibility; RSR swaps the action's TargetType to HighHP as a fallback when the target is immune to both.
  - **Geomancer:** weather-dependent buffs (Sunbath, Cloudy Caress, Blessed Rain, Aetherial Gain…) — RSR lets per-action `StatusProvide`/CanUse checks handle it.
  - **Dancer:** `Dance` opens a proc chain (Sword Dance / Tango / Jitterbug / Waltz statuses).
  - **Time Mage:** Occult Comet has swiftcast synergy (fires instantly under Swiftcast / PLD Requiescat); Occult Quick withheld when the main job already has a haste/instant proc up.

---

## 2. Daedalus design

### Placement — a cross-job layer, NOT per-job modules
Per CLAUDE.md: duty actions "sit on top of the main job, not part of per-job modules." The layer lives in its own folder and is invoked from `BaseRotation` for **every** job, the same way `RoleActionPushers` provides cross-job pushes today.

```
Daedalus/
  Services/Occult/
    PhantomJobService.cs        — territory gate, active job + level (status stacks), duty-slot reads
    PhantomItemTracker.cs       — inventory counts for 47740/47741/47743 (+ Ether item)
  Rotation/Phantom/
    PhantomActions.cs           — action IDs (incl. slot variants), status IDs, item IDs
    PhantomConfig.cs            — toggles + thresholds (persisted)
    PhantomActionLayer.cs       — the scheduler pusher (entry point called by BaseRotation)
    Handlers/
      SurvivalHandler.cs        — Resuscitation, Knight heals, Chemist items, Occult Chakra
      UtilityHandler.cs         — interrupts, dispel, sprint, Featherfoot, party buffs (Aria, March, Rime, Bell)
      DamageHandler.cs          — Cannoneer/Berserker/MysticKnight/Gladiator/Samurai/TimeMage damage
      OraclePredictionHandler.cs — deck state machine (port of RSR logic)
      DancerProcHandler.cs      — dance chain
      GeomancerWeatherHandler.cs — weather buff set
  Windows/Config/ OccultSection — config UI (per-phantom-job groups, like RSR's PhantomJob-tagged configs)
```

### Detection (PhantomJobService)
- `IsInOccultCrescent` — TerritoryType **1252** now, held in a `HashSet<ushort>` so North Horn (7.55) is a one-line addition. Refresh on `OnTerritoryChanged` (hook already exists on `BaseRotation`).
- `ActiveJob` + `Level` — status-stack scan identical to RSR (`stacks == 255 → 0`). Status IDs pulled from Lumina at implementation time and cross-checked against RSR's `StatusID` enum.
- `IsSlotted(actionId)` — read the duty action slots via ClientStructs `DutyActionManager` (same source RSR's `IsOnSlot` uses). **Fail closed:** action not visible on a slot ⇒ never push it.
- Everything null-safe during zone transitions (known crash pattern — follow `TrustPartyRoleHelper` guard style).

### Execution — scheduler priority bands
`PhantomActionLayer.Push(context, scheduler, isMoving, inCombat)` is called from `BaseRotation.ExecuteModules` (single call site, before the module loop), active only when `PhantomJobService.IsInOccultCrescent`. It pushes into the SAME per-rotation scheduler the job modules use, so normal dispatch/weave rules apply:

| Band | Content | Priority intent |
|------|---------|-----------------|
| Emergency | Occult Resuscitation (self HP < threshold), Chemist potion/elixir, Occult Chakra heal | ahead of job damage, behind job emergency heals |
| Utility oGCD | interrupts (Mineuchi/Falcon), Occult Dispel, mits (Phantom Guard/Defend/Shirahadori) | normal weave slots |
| Party buffs | Offensive Aria, Mighty March, Hero's Rime, Battle Bell, Phantom Aim, Dance | weave, in combat only |
| Damage GCD | cannons, spellblades, Deadly Blow, Iainuki/Zeninage, Occult Comet, prediction cards | AFTER the main job's filler priorities — phantom damage only fires when the job has nothing better (matches RSR putting them in GeneralGCD last) |

**Universal guards (every push):**
1. `IsInOccultCrescent` + `IsSlotted` + phantom level ≥ unlock (from `PhantomJobService` — never a raw MinLevel check; duty actions bypass `ActionAvailability`'s job-level model entirely).
2. **Lockout:** Daedalus equivalent of `HasLockoutStatus` — a per-job "don't stomp my burst GCD" status list (MCH Reassembled, RDM mana stacks, VPR refresh, SAM Midare-ready, etc.). Start from RSR's list, keep it in `PhantomActions.LockoutStatusIds`.
3. **Burst hold:** damage band checks `PartyCoordinationService.GetBurstWindowState()` / job burst flag when `SaveForBurstWindow` is on (default on) — survival/utility exempt.
4. Cast-time phantom actions (Occult Comet, cannons, Predict cards where applicable) respect the existing `isMoving` / `MechanicCastGate` conventions.
5. Consumable actions check `PhantomItemTracker` first.

### Config surface (trimmed from RSR's)
- Master toggle: **Enable Phantom Actions** (default ON in zone).
- **Save damage actions for burst** (default ON).
- Freelancer: Resuscitation HP% (0.70). Knight: Pray-as-heal, Pledge self/target. Monk: Phantom Kick max range (5y), Chakra MP/HP thresholds. Chemist: potion/ether self-only + thresholds, elixir party-HP% (0.30). Oracle: per-card enables, Save-Invuln-for-Starfall (ON), heal-predict thresholds (0.70/0.50). Cannoneer: dark/shock preference + immune fallback. Geomancer: Suspend in/out of combat (OFF).
- Rendered as collapsible per-phantom-job groups; only the ACTIVE phantom job's group expanded by default.

### Debug
Debug window gets an **Occult tab**: territory flag, active phantom job + level, slotted action list, lockout status live view, Oracle deck state, item counts. (Static-backed — config-copy lesson applies.)

---

## 3. Phases

| Phase | Scope | Verify |
|-------|-------|--------|
| **1 — Detection** | `PhantomJobService` + item tracker + Debug Occult tab. NO action firing. | In-game in South Horn: correct job/level/slots/items shown across job swaps + zone in/out |
| **2 — Catalog + config** | `PhantomActions` IDs from Lumina (cross-check RSR), `PhantomConfig` + UI section | IDs resolve in Lumina sheets; config persists; unit tests on gating helpers |
| **3 — Survival + utility executor** | `PhantomActionLayer` wired into `BaseRotation`; Survival/Utility/Party-buff bands; lockout + slot guards | Field test: heals/mits/interrupts fire, zero main-rotation regressions outside the zone (layer inert when territory gate false) |
| **4 — Damage band** | Cannoneer strategy, spellblades, Berserker, Samurai, Occult Comet swiftcast synergy, burst hold | Field test on FATE trash + CE boss; confirm no buffed-GCD stomping |
| **5 — State machines** | Oracle deck (port RSR incl. Starfall+Invuln combo), Dancer procs, Geomancer weather | Field test with Oracle + Dancer equipped |

Each phase: minimum 4 regression tests per module (house rule), both build configs, full suite green.

## 4. Open items to resolve during Phase 1
- Exact **status IDs** for the 16 phantom-job level statuses and `PredictionOf*` / dance-proc statuses (Lumina + RSR cross-check).
- Exact **duty action execution path**: phantom actions are real Action-sheet entries fired via `ActionManager.UseAction` with the normal ActionType — confirm `GetActionStatus` behaves for slotted duty actions (expected yes; RSR's `CanUse` relies on it) so the scheduler's dispatch gate works unchanged.
- Confirm ClientStructs **DutyActionManager** slot array shape on current Dalamud API 15.
- North Horn (7.55): keep everything keyed off the territory set + data tables; expect new jobs (RSR already lists Gladiator/Dancer/MysticKnight added post-7.25).
