# Daedalus — CLAUDE.md
> Load this file at the start of every session. It is the source of truth for project conventions, architecture, and workflow.

## Project Overview
Daedalus is a Dalamud plugin for Final Fantasy XIV providing automated rotation execution for multibox play (4–8 synchronized toons). Target content: Pandaemonium P3S/P4S Savage and Extreme trials. Built on a forked RSR (RotationSolverReborn) codebase, heavily reworked for Dawntrail patch accuracy and multi-instance IPC coordination.

**Repo:** `D:\Dev\Daedalus`
**Solution:** `Daedalus.sln`
**Test project:** `Daedalus.Tests`
**Plugin entry:** `Daedalus/Plugin.cs`
**Burn reference docs:** `.cursor/rules/burn-reference/` (current patch rotation references per job — always check before implementing rotation logic)

## Versioning
- Current: `0.x.x` during implementation
- Target: `v0.1.0` when all planned jobs are complete
- Target: `v1.0.0` when full 8-toon IPC + LAN coordination is stable

## Naming Conventions — Greek Pantheon
Every job implementation is named after a Greek deity. Never use job names directly as class prefixes.

| Job | Codename | Status |
|-----|----------|--------|
| PLD | Themis | Trust-validated |
| WAR | Ares | Code-complete, Trust pending |
| GNB | Hephaestus | Rotation fixed, Trust pending |
| DRK | (TBD) | Not started |
| SAM | Nike | Trust-validated |
| NIN | Hermes | Burst gaps filled, Lv58 dungeon testing (Lv100 pending) |
| MNK | (TBD) | Not started |
| DRG | (TBD) | Not started |
| RPR | (TBD) | Not started |
| VPR | (TBD) | Not started |
| BRD | (TBD) | Not started |
| MCH | Prometheus | In progress (overnight build) |
| DNC | (TBD) | Not started |
| WHM | (TBD) | Not started |
| SCH | Athena | Scoped (FairyModule implemented) |
| AST | Astraea | Trust-validated |
| SGE | Asclepius | Trust-validated |
| PCT | (TBD) | Trust-validated |
| BLM | (TBD) | Not started |
| SMN | (TBD) | Not started |
| RDM | (TBD) | Not started |
| BLU | Proteus | Scoped, not started |

## Architecture

### Scheduler Module Pattern
Every job follows this structure — never deviate from it:

```
Rotation/{CodenameCore}/
    Modules/
        DamageModule.cs       — GCD priority chain
        BuffModule.cs         — oGCD / burst cooldown logic
        HealingModule.cs      — healer jobs only
        MitigationModule.cs   — tank jobs only
    {Codename}Actions.cs      — action ID constants + Get* helpers
    {Codename}Context.cs      — job-specific context interface
    {Codename}Config.cs       — persistent config (ImGui toggles)
```

Modules implement `IAstraeaModule` (healers) or `IThemisModule` (tanks) or equivalent base. Every module has a `Priority` constant — check `ModulePriorityTests` before assigning.

### Key Infrastructure
- **`ActionAvailability`** (`Services/Action/ActionAvailability.cs`) — universal level + learned check. Always use `Pick`, `FirstAvailable`, or `MeetsLevelAndLearned` instead of hardcoded level checks. Never bypass this.
- **`ChargeGcdSubmitGuard`** — prevents double-submission of charge-based GCDs within one GCD window. Applied in `ExecuteGcd`, `ExecuteGcdRaw`, `NotifyActionExecuted`. Any charge-based GCD (GetMaxCharges > 1) automatically hits this floor.
- **`ReplacementBaseId`** — used for combo chain actions (Prominence, Atonement chain, Generation chain in VPR). Prevents ActionStatus deadlock. Required for any action that replaces another via button substitution.
- **`IsActionLearned`** — wired across all 18 `*Actions.cs` files. Always pass `context.ActionService` to `Get*` helpers so they walk the upgrade chain correctly.
- **`TankTargetingHelper`** — `ResolveEnemyStrategy` for add management. `PullRangedMobsWithRangedAttack` and `IgnoreAddsWithCoTank` flags.
- **`PartyCoordinationService`** — IPC burst window state. `GetBurstWindowState().IsActive` gates burst-aware abilities (Phlegma, Intervene charge holds etc).

### Build Baseline
- **Warnings:** 18 (do not increase — fix any new warnings before committing)
- **Tests:** 3095 passing (as of last commit — always run full suite before committing)
- **Test project:** `dotnet test Daedalus.Tests`
- **Build BOTH configurations — always:** `dotnet build Daedalus.sln -c Debug` AND `dotnet build Daedalus.sln -c Release`. Both must succeed with 0 errors before any change is considered done. Never verify only one configuration — Release is what ships, Debug is what the user runs in-game during testing, and config-specific compile failures (e.g. `#if DEBUG` blocks, conditional analyzers) only surface when both are built.

Never commit with failing tests. Never commit with warnings above baseline.

## Rotation Implementation Workflow
For every new job, follow this exact sequence:

1. Read `.cursor/rules/burn-reference/{job}-rotation.md` — this is current patch ground truth
2. Diff Daedalus existing implementation (if any) vs RSR reference vs burn-reference doc
3. Identify correctness bugs (fix first) vs optimization gaps (fix after Trust validation)
4. Implement using existing job as template (SGE/AST for healers, PLD/GNB for tanks, SAM/NIN for melee)
5. Add regression tests for every new behavior — minimum 4 tests per module change
6. Run full test suite — must be green
7. Trust dungeon validation (Origenics or equivalent) — check combat log for spam, idle gaps, DoT uptime
8. Commit with descriptive message citing patch version and what changed

## Known Patterns and Gotchas

### ABC (Always Be Casting)
GCD uptime is the single biggest DPS factor. The ActionService stale-guard timers (`UncommittedSubmitStaleSeconds`, `PartialRecastStaleSeconds`, `FailedSubmitBackoffSeconds`) control how long the system waits before retrying after a failed/partial GCD submit. Keep these tight — any gap > 1.5s between GCDs in combat is a bug unless the player is moving for mechanics.

Key ABC patterns:
- `_gcdSubmittedThisCycle` clears when `GcdRemaining` reaches 0 or stale timers expire. If it stays latched too long, ABC breaks.
- Short-GCD jobs (MCH Heat Blast 1.5s) need `GetAvailableWeaveSlots` to drop the queue reserve so one oGCD weave fits between Heat Blasts.
- `_ogcdsUsedThisCycle` must reset when `GcdRemaining` jumps up (new GCD cycle via queue window), not just when it hits 0.
- Combat end markers in the action log (`EndCombat`) show duration + uptime% — use these to spot ABC gaps between pulls vs within pulls.

### Charge-Based Ability Spam
The `_lastChargeBasedGcdSubmitUtc` latch in ActionService prevents double-submission. If a charge ability still spams, check:
1. Is it going through `ExecuteGcd` or bypassing via raw submit?
2. Does it have a `ChargeHoldPolicy` set?
3. Is `GetMaxCharges` returning > 1 for this action?

### DoT Uptime Checks
Always use `FindEnemyNeedingDot` (same pattern as WHM/AST) — never push a DoT without checking if it's already applied. Check all rank status IDs (e.g. SGE Eukrasian Dosis: 2614/2615/2616). Single status ID DoT checks are a known bug pattern.

### Shared Resource Conflicts
If two modules can both consume the same buff (e.g. SGE Eukrasia used by both shield path and DoT path), the consuming module must yield via a priority check. `ShieldHealingHandler` yields to `DamageModule` when DoT maintenance is pending. Follow this pattern for any shared buff.

### AoE Combo Lock-In
Once an AoE combo starter fires, the finisher is unconditional — never re-evaluate target count mid-combo. Gate the decision to START the combo on target count; gate the finisher on combo state only. (PLD Total Eclipse → Prominence, WAR Overpower → Mythril Tempest etc.)

### Proc Gates
Never fire Royal Authority (PLD) or equivalent combo finishers while any proc is active (`HasUnspentFillerProcs()`). Same for Total Eclipse when `AtonementStep > 0`. Unspent procs block combo restart.

### FoF / Burst Timing
Fight or Flight (PLD) fires immediately off cooldown regardless of combo position — do not gate on `ComboStep`. Same principle applies to any burst cooldown that should never drift.

### Addersgall / Charge Overcap
Addersgall (SGE) uses `FindEnemyNeedingDot` equivalent for healing. Druochole fires when 1 target below 99% HP, Ixochole fires when 2+ targets below 85% (dungeon threshold — not 3, since only 4 players in dungeon). Never hardcode 3-target threshold for 4-man content.

### Tank Behavior
- `TankConfig.PullRangedMobsWithRangedAttack` — suppress gap closer, use ranged GCD only (Lightning Shot/Shield Lob/Tomahawk/Unmend). Default false — opt-in.
- `TankConfig.IgnoreAddsWithCoTank` — stick to hard target when co-tank present. Default false.
- Royal Guard / tank stance maintenance — always verify on zone-in.

### Nav / Movement
- vNav Flex slider (0.0–2.0y, default 0.5y) — deadband for movement commands. Only issue vNavmesh call when outside `threshold ± vNavFlex`.
- Movement is ONE-DIRECTIONAL for melee — move IN when outside range, do NOT move away when too close (RSR pattern). Two-directional positioning causes oscillation.
- `threshold = enemyHitbox + playerHitbox` — no additional flex constant needed, game bakes in 3y reach.
- Solo mode: disable positionals and max melee positioning entirely.
- Debug rings: Enemy hitbox (inner), Combined melee (enemyHitbox + playerHitbox), Max melee (combined + vNavFlex).

### Global Nav Window
Nav settings are global, not per-job. Located in Nav Control window:
- vNav Flex slider
- Solo Position Lock toggle
- Max Melee Debug Rings toggle
- Add Pull toggle (tank)
- Tank Mode toggle (boss anchor vs add puller)

## IPC Architecture
- **Local IPC:** Dalamud IPC between instances on same machine (already scaffolded for burn coordination)
- **LAN coordination:** UDP broadcast on local subnet (same VLAN, UniFi/Dream Machine Pro). Port TBD (~47200). `LanCoordinator` class owns UDP socket on background thread. `CoordinationBus` unifies local IPC and LAN messages — rotation modules subscribe to bus, don't care about source.
- **Message types:** role assignment, enmity sharing, tank swap, add spawning, burn signal
- **Status:** LAN layer not yet implemented. Do not enable IPC in Trust validation runs.

## Pending Plugin: Caduceus
Standalone mouseover healing plugin — separate from Daedalus rotation. Scoped but not started.
- TargetSwapExecutor pattern (store hard target → write heal target → fire → restore within 1-2 frames)
- All four healers: WHM/SCH/SGE/AST
- Custom party frames, configurable keybinds, auto-triage logic

## Rename (Olympus → Daedalus) — Complete
- Solution: `Daedalus.sln`; projects: `Daedalus/Daedalus.csproj`, `Daedalus.Tests/Daedalus.Tests.csproj`
- Namespaces: `Daedalus.*`
- Dalamud manifest: internal/display name `Daedalus`, version `0.1.0`
- GitHub repo rename to `ofnature/Daedalus` is deferred (local folder may still be `Olympus` until renamed)

## Dalamud / ClientStructs Notes
- SDK: `Dalamud.NET.Sdk/15.0.0` — do not upgrade to 15.0.2.2
- `ANTHROPIC_API_KEY` env var warning: if set, Claude Code will bill API rather than Pro subscription
- After every Dalamud maintenance bump: smoke test ClientStructs reads (GNB cartridges, SGE Addersgall, combo state, job gauges, status lists)
- `DrawCanvas` fullscreen overlay: if clicks ever pass through UI set `InhibitAtkCollision = false`
- `IsHovered` ImGui calls: gate on `IsOpen && DrawConditions()` to avoid hover reads on closed windows
- Known crash pattern: plugins reading player/party structs during zone transition before structs are fully initialized. `TrustPartyRoleHelper.FindTankInParty` has null guard — other object table iterators should follow same pattern.
