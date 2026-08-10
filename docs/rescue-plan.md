# Rescue plan — LAN-timed "pull the toon that won't make it"

> Scoped 2026-08-10 (user request). Status: **P0 DONE 2026-08-10** — pure policies
> (`Services/Rescue/`: RescueBroadcastPolicy, RescuePolicy, RescueElection), wire types
> RescueNeeded=18 / RescueClaim=19 + payloads, CoordinationBus events/broadcasts (party-group
> scoped), 37 tests. Nothing calls the senders yet — next session starts at Phase 1.

## Goal

When a fleet toon is standing in a telegraphed AoE and is **not going to clear it before it
resolves**, a healer in its party casts **Rescue (7571)** and yanks it to the healer's
position. The decision needs timing that only the endangered toon has (its own BossMod hints),
so the trigger rides the LAN: endangered toon broadcasts "I won't make it", healer validates
its own side and pulls.

Everything scaffolded today says "don't": `RoleActionConfig.RescueMode` is clamped to 0
(Manual) with the comment *"Automatic rescue is not implemented due to extreme risk."* This
plan is what makes automatic mode buildable without earning that comment — the risk is managed
by splitting the decision so each side only asserts what it can actually know, and by shipping
dark (telemetry + dry-run first, auto-fire last, default OFF forever).

## What already exists (do not rebuild)

| Piece | Where | Notes |
|---|---|---|
| Rescue action def | `Data/RoleActions.cs` (7571, Lv48, 30y, 120s oGCD) | Also in `ActionIds`, checklists, ActionLibrary |
| Config + UI scaffold | `Config/RoleActionConfig.cs` (`EnableRescue`, `RescueMode` clamped 0..0), `GeneralSection.DrawRoleActions` warning text | Unlock the clamp in Phase 2, not before |
| BMR safety reads | `Services/Positional/Navigation/BossModSafetyService.cs` — `IsPositionSafe(Vector3)` (arbitrary positions), `ForbiddenZoneActivationInSeconds`, `NextDamageInSeconds`, `AiNaviTargetPos` | The load-bearing primitives, already subscribed |
| LAN transport | `Services/Network/` — `LanMessage` envelope (extend-only JSON, `g` party-group scoping), `CoordinationBus` (framework-thread pump, dedup ring, `InjectForTest` receive seam) | Next free wire ids: **18, 19** |
| Double-cast prevention template | `BaseResurrectionModule` + `PartyCoordinationService` raise reservation (`IsRaiseTargetReservedByOther`, reserve-before-cast) | Rescue claim mirrors this exactly |
| Party-target dispatch | Raise modules push by `GameObjectId` through the scheduler — no hard-target swap needed for **living** party members | Phantom raise's swap dance is corpse-specific |

Prereqs inherited from the LAN stack: coordinator enabled on every box (the UDP loopback
mirror IS the same-machine transport — Dalamud IPC never crosses processes),
`EnablePartyCoordination` master toggle on, firewall rule for cross-machine. LAN Phase 0
two-machine test still pending; rescue dry-run can double as part of it.

## The architectural rule that makes this safe

**The endangered toon decides "I am in danger" against ITS OWN hints. The healer never
evaluates the target's position.**

This is not a convenience — it is the tower/soak correctness rule. BMR builds AIHints per
player: a tower or stack the local player is assigned to soak is NOT a forbidden zone in that
player's hints, but it IS forbidden in everyone else's. A healer probing
`IsPositionSafe(targetPos)` against its own hints would read every deliberate soaker as a
rescue candidate and pull them out of the tower. With the split, the soaker's own hints say
"safe", it never broadcasts, and the failure mode cannot occur.

The healer asserts only what it can know locally:
- its own position is genuinely safe to pull INTO (own `IsPositionSafe` + activation margin),
- Rescue is learned/enabled/off cooldown, target in range, alive, not knockback-immune,
- the request is fresh (TTL) and unclaimed.

## Timing budget

| Leg | Cost |
|---|---|
| Danger sampled on sender frame | ~16–33 ms (frame cadence) |
| LAN broadcast, same subnet / loopback | < 5 ms |
| Healer picks it up next framework tick | ~16–33 ms |
| Scheduler push → server ack → pull lands | ~100–300 ms |
| **End-to-end signal → yank** | **~150–400 ms** |

Two thresholds fall out of this:

- **`RescuePanicSeconds` (default 2.0)** — sender starts broadcasting when it is still unsafe
  with ≤ this long to activation. BMR steering normally clears zones with seconds to spare; a
  toon still in the bad at T-2.0 is stuck, casting, pathing long, or navmesh-trapped. That IS
  the "won't make it" signal — no pathfinding introspection needed for v1.
- **`RescueAbortSeconds` (default 0.4)** — healer refuses to fire with less than this left.
  Below the end-to-end budget the pull lands at or after resolution; the server snapshots the
  target's position when the effect applies, so a too-late Rescue rescues nobody and burns
  120s of cooldown.

## Wire protocol (extend-only, party-group scoped via envelope `g`)

**`RescueNeeded = 18`** — sender: any toon whose local policy fires.
`{ eid, act, px, py, pz, kb }` — entity id, ms-to-activation, position, knockback-immunity
flag (sender reads its own Surecast/Arm's Length; healer double-checks the status list
locally anyway — ids to verify at build time). Re-broadcast every ~250 ms while the condition
holds; receivers treat a request older than **750 ms** as expired (the toon escaped or died —
never act on stale danger).

**`RescueClaim = 19`** — `{ eid, by }`. The firing healer claims immediately before pushing;
a seen claim suppresses other healers for 3 s. Same shape as the raise reservation, and the
same per-target guard goes in `PartyCoordinationService` so same-machine dedup is free.

**Multi-healer election** (each healer knows only its OWN eligibility, so no shared vote):
rank = index among the party's healers sorted by SenderId (roster is already deterministic on
every machine). Rank 0 fires as soon as eligible; rank N waits `N × 300 ms` and fires only if
no claim has appeared. An ineligible rank-0 (dead, cooldown, unsafe spot) simply never fires
and rank 1 covers after one backoff step.

## Sender-side policy (pure class: `RescuePolicy`, fully unit-testable)

Broadcast when ALL hold:
1. in combat, BMR available, duty has an active module,
2. `!IsPositionSafe(own position)` for ≥ `MinUnsafeSamples` consecutive frames (default 3 —
   debounces single-frame hint flickers),
3. `ForbiddenZoneActivationInSeconds ≤ RescuePanicSeconds`,
4. per-toon `BroadcastRescueNeeded` toggle on (opt-out for special-duty toons),
5. not currently under forced-movement/dash of its own (skip while `AiIsNavigating` reports a
   dash — verify what BMR exposes; conservative: suppress if moving faster than run speed).

Stop broadcasting the frame any of these goes false. v2 refinement (deferred): true escape
ETA — probe `IsPositionSafe` along the BMR navi-target direction and compare distance/speed
(6.0 y/s run, 7.8 sprint) against activation, so a toon that WILL make it never panics even
inside the window. v1's "still in the bad at T-2" is simpler and the dry-run data will show
whether it over-triggers.

## Healer-side gates (ALL must hold at fire time, re-checked the same frame as the push)

1. `EnableRescue && RescueMode == Auto` (mode unlocked in Phase 2), Rescue learned, off CD,
2. request fresh (< 750 ms) and unclaimed; election rank satisfied,
3. target alive, in local party group, within **29 y** (1 y inside the real 30 to survive
   server-side position drift),
4. target NOT knockback-immune (Surecast / Arm's Length make Rescue a no-op — verify status
   ids at build; sender's `kb` flag is a hint, the healer's local status read is the gate),
5. own position safe: `IsPositionSafe(self)` AND `ForbiddenZoneActivationInSeconds >
   RescueDestSafetySeconds` (default 2.5) — "safety" that activates in 1 s is not safety,
6. `|targetY − selfY| ≤ 3 y` — ledge/platform guard; BMR models zones, not geometry, and a
   cross-height pull is exactly how Rescue kills people,
7. `act` still > `RescueAbortSeconds`,
8. per-target re-pull cooldown 10 s (a toon that re-enters the bad twice in ten seconds has a
   problem Rescue can't fix — don't chain-yank it).

Execution: claim → push Rescue as an **oGCD at emergency-sustain priority** through the
healer's existing scheduler with the target's `GameObjectId` (raise-module precedent; living
party members dispatch fine by id). If field testing shows the dispatch needs a hard target,
fall back to a store/swap/fire/restore identical to the phantom-raise pattern — including its
hard-won lesson: latch nothing before the push confirms.

## Known failure modes → where they're handled

| Failure | Mitigation |
|---|---|
| Pulling a deliberate soaker out of a tower | Sender-decides rule — soaker's own hints are safe, no broadcast |
| Pulling INTO danger | Healer dest gates (5) |
| Pull lands after the AoE resolves | Abort threshold (7) + TTL |
| Two healers double-pull | Claim + ranked backoff |
| Yanking a kb-immune toon (wasted 120 s CD) | Gate (4) |
| Ledge / cross-platform death pull | Height gate (6) — residual risk on same-height gaps; documented limitation |
| Chain-yanking a stuck toon | Per-target cooldown (8) |
| Splitting a stack marker by pulling mid-mechanic | **Accepted residual risk in v1** — BMR hints don't distinguish; this is why auto mode defaults OFF and per-toon broadcast is opt-out-able |
| Hint flicker false positives | Debounce (sender 2) + dry-run phase measures the real rate before anything fires |

## Phases

**P0 — pure logic + tests.** ✅ DONE 2026-08-10. `Services/Rescue/RescueBroadcastPolicy`
(sender gates + panic/debounce constants), `RescuePolicy` (healer gates, ordered
first-blocker reasons, PhoenixDownPolicy style), `RescueElection` (ordinal rank + 0.3s
backoff steps). `LanMessageType.RescueNeeded=18` / `RescueClaim=19`,
`LanRescueNeededPayload` (`e/a/x/y/z/k`) + `LanRescueClaimPayload` (`e`),
`CoordinationBus.BroadcastRescueNeeded/BroadcastRescueClaim` + `OnRescueNeeded/OnRescueClaim`
events, both `IsForLocalGroup`-scoped. Tests: policy gates, election determinism, payload
round-trips, receive path via `InjectForTest` incl. cross-party drop + zero-group legacy
reach. Nothing calls the senders — inert until Phase 1.

**P1 — telemetry + dry run.** Sender detector wired into the frame loop (reads
`BossModSafetyService` already polled for movement), broadcasts `RescueNeeded`; healers run
every gate and LOG the verdict — `"WOULD Rescue Escha (act 1.4s, dist 18y, rank 0)"` — into
the action log + a `RescueState` line on Debug ▸ Duty for both roles. Field-validate across
Occult CEs and a Trust dungeon. **Exit gate: zero would-pulls on soaks/towers, false-positive
rate understood.** This phase needs no RescueMode change and can ship early.

**P2 — live execution.** Unlock `RescueMode` clamp to 0..1 (Manual/Auto), wire claim + push,
config UI under the existing Rescue section (panic-window and dest-safety sliders, per-toon
broadcast toggle) keeping the red warning text. Default stays Manual. Changelog entry.

**P3 — later refinements.** Escape-ETA panic (v2 sender policy), stack/spread awareness if
BMR ever exposes mechanic intent, geometry-aware dest checks, suppress-while-dashing polish.

## Open questions (resolve during P0/P1, none block starting)

- Exact status ids for Surecast / Arm's Length knockback immunity (XIVAPI-verify; do NOT
  trust memory — the Cure II id-neighbourhood lesson applies).
- Whether Rescue on a living party member dispatches clean by `GameObjectId` from every healer
  job's scheduler (expected yes via raise precedent; P1 dry-run can't test it — first P2
  field run must watch for it).
- Whether `AiNaviTargetPos` is reliable enough to add "toon is actively pathing OUT" as a
  broadcast suppressor, or whether that belongs with the v2 ETA work.
- Does `ForbiddenZoneActivationInSeconds` report the GLOBAL soonest activation rather than the
  zone under the toon? (It's a single float via IPC.) If global, a far-away early zone could
  understate the toon's actual time budget → acceptable for v1 (errs toward earlier panic),
  note for v2.
