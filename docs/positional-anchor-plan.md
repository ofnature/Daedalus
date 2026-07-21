# Positional Anchor Plan — boundary camping as the primary positional mode

> Cold-start-ready execution plan (2026-07-20). Origin: field verdict that wiring positionals
> into BMR/VBM "is not working". Decision: **Daedalus owns the angle, BMR owns safety** — the
> melee toon PARKS at the flank/rear boundary, offset ~10° into the arc of the NEXT positional,
> so every positional swap is a slight step across the border and back.

## The core idea (user's words)
"Melee with positional actions sit at the nearest 10-degree border to the next positional so
it's a slight step there and back."

Geometry: the flank/rear boundaries sit at ±135° from the target's facing. A toon parked at
`boundary ± 10°` is *inside* the required arc for the next positional, and the OTHER arc is only
~20° of arc away — at melee stand distance (~4-5y) that's a **1.2-1.7y chord hop**, walkable in
a fraction of one GCD, with an equally short hop back when the requirement flips.

## What already exists (do NOT rebuild)
| Piece | Where | State |
|---|---|---|
| Boundary-biased stand math | `PositionalStandCalculator.GetStandDirectionUnit` (`BoundaryBiasRadians`) | DONE — picks the boundary on the player's current side, biases into rear or flank arc |
| Bias config + UI | `NavConfig.PositionalBoundaryBiasDegrees` (default 10°, clamp 0-30) + Nav Control slider | DONE |
| Orchestration | `PositionalMovementService.Update` → `TryQueuePositionalArc` (BMR safety veto → MovementArbiter/vNav) | DONE |
| GCD movement budget clamp | `PositionalStandCalculator.ComputeMaxHorizontalMoveYalms` + "would clip GCD" skip | DONE |
| Per-job next-positional providers | `*PositionalAnticipationProvider` — wired in Hermes(NIN), Nike(SAM), Thanatos(RPR), Echidna(VPR), Zeus(DRG), Kratos(MNK) | DONE (but see stale-positional cleanup below) |
| BMR handoff | `PositionalSnapshot.BoundaryCampingActive` → BMR `DesiredPositional=Any` (BMR keeps range/dodges only) | DONE |
| Safety veto + yield | `BossModSafetyService.QueryPositionSafety`/`IsSegmentSafe`; `MovementArbiter` yields to BMR dodges | DONE |
| Solo/party gate | `IsAutoMovementAllowed()` (auto-move only with a party; solo never paths) | DONE — keep |
| Single-enemy gate | `PositionalRequirementHelper.ShouldApply` (packs skip positionals) | DONE — keep |

**Why it's dark today:** `NavConfig.EnableBoundaryCamping` defaults **false** (v0.1.8 shipped it
off pending validation) and `IsPositionalArcRolloutEnabled` is **true only for Hermes/NIN**.

## Decisions
1. **Drop the BMR/VBM positional path entirely.** No `Hints.RecommendedPositional` reads, no BMR
   AI positional ownership. When camping is live BMR gets `DesiredPositional=Any` (already
   implemented); `BmrAiConfigPolicy` must assert this on every config push so a BMR preset can't
   silently take the angle back.
2. **Boundary camping becomes the standard positional mode** for the FIVE jobs that still HAVE
   positionals on live: **NIN** (Aeolian rear / Armor Crush flank), **SAM** (Gekko rear / Kasha
   flank), **RPR** (Gallows rear / Gibbet flank), **VPR coils** (Hunter's flank / Swiftskin's rear),
   **MNK coeurl form** (Demolish REAR; Snap Punch + Pouncing Coeurl FLANK — opo-opo and raptor
   GCDs have none, so MNK's anchor is only live while raptor form is up → coeurl is next), and
   **DRG combo steps** (Chaotic Spring / Chaos Thrust REAR, Fang and Claw FLANK, Wheeling Thrust
   REAR; Drakesbane / Heavens' / Spiral Blow none — anticipation is combo-position based, dual
   ids per step; `Zeus.ComputeNextPositional` implemented 2026-07-20).
   ⚠️ Both MNK and DRG were previously believed positional-free — user tooltips + live sheets
   (XIVAPI ActionTransient) disproved the 7.05-era RSR checkout on 2026-07-20. **Re-verify the
   whole positional bank against the live sheet after every patch; the RSR checkout is a snapshot,
   not truth, for game data.** Only the VPR finisher family remains positional-free (7.05) —
   the coils are Hunter's FLANK / Swiftskin's REAR (sheet-confirmed same day).

## Work items

### P1 — Anchor persistence (the actual gap) — ✅ DONE 2026-07-20
Implemented as specified below: `AnchorDriftToleranceYalms = 1.5f`, drift branch in
`PositionalMovementService.Update` (bias-gated, budget-clamped, GCD-clip guarded, BMR safety
veto via the same `TryQueuePositionalArc` path), skip reason "at anchor" when parked. 6 tests
incl. the flank↔rear hop-chord geometry proof (≈1.7y at 10° bias). Inert until a job's rollout
gate + `EnableBoundaryCamping` flip (P3) — safe to ship dark.
Today `PositionalMovementService.Update` idles with "already at positional" whenever the toon is
anywhere inside the required arc. After a knockback/dodge that leaves the toon at the arc CENTER
(45° from the border) or on the far side, the next swap is a long walk — exactly what the anchor
is meant to prevent.
- Replace the `IsAlreadyCorrect → idle` branch with: correct-arc BUT farther than
  `AnchorDriftToleranceYalms` (new constant, suggest ~1.5y ≈ vNavFlex×3) from the boundary-biased
  stand point → queue a drift move back to the anchor, movement-budget-clamped, lowest urgency
  (never when it would clip a GCD, never during Hyperphantasia-like cast states, always behind
  the BMR safety veto).
- The "step back after the positional" needs NO new code: once the positional GCD lands, the
  provider's next requirement flips arcs, the stand point moves ~20° across, and the same drift
  logic walks it. Verify with tests that the sequence anchor→cross→anchor produces two short
  hops (< 2y each), not arc-center round trips.

### P2 — Provider correctness pass — ✅ DONE 2026-07-20
Verified/fixed per job (all six live-sheet-checked the same day):
- NIN ✅ provider reuses `HermesKazematoiRules.GetFinisherPositional` (the dispatcher's chooser) — already parity.
- SAM ✅ provider mirrors Jinpu/Shifu refresh + Meikyo missing-Sen routing — already parity (tests existed).
- RPR ✅ Enhanced Gibbet(flank)/Gallows(rear) statuses; null pre-pair is CORRECT — the module is
  position-adaptive for the first reaver (casts whichever arc you're in), so the anchor idles and
  the Enhanced status drives the next hop.
- VPR ✅ FIXED: map now covers the twinblade chain (Dreadwindy/HunterCoilReady→FLANK,
  SwiftskinCoilReady→REAR, outranks the finisher step) + venom-driven finishers. **Live-sheet
  correction: the finisher family HAS positionals (Flank* FLANK / Hind* REAR 340→400) — the
  "lost in 7.05" note was the stale-snapshot trap again.** `Echidna.ComputeNextPositional` testable static.
- MNK ✅ / DRG ✅ done earlier same day (b25a6bf / 6b2b1cc).
Whole bank live-sheet-verified 2026-07-20: NIN Aeolian-REAR/ArmorCrush-FLANK, SAM Gekko-REAR/
Kasha-FLANK, RPR Gibbet-FLANK/Gallows-REAR, VPR coils+finishers, MNK coeurl, DRG combo steps.
For each of NIN/SAM/RPR/VPR: the provider must answer "which arc does the NEXT positional GCD
need, and how many GCDs away is it?" from live combo/gauge state:
- NIN: Aeolian Edge (rear) vs Armor Crush (flank) from the Huton/Kazematoi decision the
  DamageModule already makes — reuse its chooser, don't re-derive.
- SAM: next Sen decides — Gekko (rear) when Getsu missing, Kasha (flank) when Ka missing;
  Meikyo alters order (finisher choice logic already exists in the module).
- RPR: pending Soul Reaver/Executioner pair — Gallows (rear) preferred outside True North,
  Gibbet (flank) when the module's chooser says so. Party-only enforcement stays.
- VPR: coil order chooser (Hunter's flank → Swiftskin's rear or the module's actual order).
- MNK: only the raptor→coeurl transition anticipates an arc (mirrors GetCoeurlAction: Coeurl's
  Fury → Flank, Demolish due → Rear, else Flank — already implemented in Kratos.GetNextRequiredPositional
  2026-07-20); all other form transitions return null.
Contract test per provider: feed a combo state, assert (arc, GCDs-until) matches what the
DamageModule would actually cast. The anticipation must never disagree with the dispatcher —
that was the failure mode of the old BMR wiring (BMR's guess vs our rotation's reality).

### P3 — Rollout & defaults flip
- Flip `IsPositionalArcRolloutEnabled => true` for Nike, Thanatos, Echidna (Hermes already true)
  ONLY after each passes a Trust/dummy validation run (see P5).
- Flip `NavConfig.EnableBoundaryCamping` default to **true** once all four are validated.
- Nav Control window: show the live anchor state per the debug rings (anchor point, current
  bias side, next-required arc) — extend the existing Max Melee Debug Rings drawing with the
  two boundary radials and the anchor dot so field debugging is visual.

### P4 — Interaction rules (write tests for each)
- **BMR dodge wins**: MovementArbiter already yields; assert the anchor drift never issues a
  vNav call while BMR `IsNavigating` owns movement, and resumes ≤1 GCD after.
- **Max-melee maintenance vs anchor**: the anchor point IS at melee stand distance; the
  max-melee back-off path must not fight it (one destination, the anchor, when camping live).
- **True North / positional immunity / packs / solo**: all existing suppressions stay; camping
  falls back to plain max-melee stand.
- **Uptime guard**: every queued move remains budget-clamped (`would clip GCD` skip stays the
  hard rule — a missed positional costs ~60 potency, a clipped GCD costs a full cast).

### P5 — Validation protocol (per job, before its rollout flag flips)
1. Striking dummy, 3 min, party of 2 (to satisfy the party gate): confirm anchor hold ±10°,
   swap hops < 2y, zero GCD clips in the action log (log-on-commit uptime% ≥ 99).
2. Trust dungeon run: confirm no fights with BMR movement during mechanics, no oscillation
   (the vNavFlex deadband should absorb target micro-rotation — watch for jitter when the boss
   turns; if the anchor churns, add target-rotation smoothing: only recompute the anchor when
   the target's facing has changed > 15° or the arc requirement flipped).
3. Positional hit confirmation: spot-check Fang and Claw-type potency lines in the parser or
   watch the in-game positional success flash; DrawCanvas rings give the visual check.

### Est. order: P2 (providers+tests) → P1 (anchor drift) → P4 (interaction tests) → P5 per job → P3 flips.

## Non-goals
- No BMR positional hints, ever (this plan replaces that approach).
- No solo-mode positionals (existing design decision stands).
- No multi-enemy pack positionals (single-enemy gate stays).
- VPR finisher family / MNK opo-opo+raptor GCDs / DRG starter+thrust steps: no positionals —
  keep them out. (MNK COEURL form and the DRG positional combo steps ARE in scope — Decisions #2.)
