# BMR Positional Handoff — diagnosis & proposed design

*Status: PROPOSAL — not implemented. Written 2026-07-05 after the movement-cadence work (arbiter, boundary camping). Boundary camping shipped default-OFF; this doc is the alternative to evaluate.*

## The problem

Melee positionals don't happen in automated play:

- **Daedalus moving the character** (vNav hops — "boundary camping") fights BMR's AI steering: BMR hard-pauses its own input-injection movement whenever a vnavmesh path runs (`.cursor/bmr/BossMod/AI/MovementOverride.cs:144`), so every Daedalus path interrupts BMR and every path-end snaps it back. Field-tested 2026-07-05: still stuttery even with the arbiter's yield/backoff cadence.
- **BMR moving to positionals itself doesn't work either** — confirmed in source, two separate reasons below.

## Why BMR won't move to a positional (verified in `.cursor/bmr`)

1. **A loaded AI preset disables positional logic entirely.**
   `BossMod/AI/AIBehaviour.cs:239` — the branch that reads `AIConfig.DesiredPositional` only runs when
   `AIPreset == null`:
   ```csharp
   else if (_config.FollowTarget && target != null && AIPreset == null)
   {
       var positional = _config.DesiredPositional;
       ...
   }
   ```
   AutoDuty loads its passive preset for every run → **all `DesiredPositional` pushes (ours via
   `BossMod.Configuration` IPC, or manual `/bmrai positional rear`) are dead on arrival.** BMR only
   follows at distance + dodges.

2. **Even preset-free, rear/flank is refused while the mob targets YOU.**
   `AIBehaviour.cs:244`:
   ```csharp
   if (positional is Rear or Flank && (target.CastInfo == null && ... target.TargetID == player.InstanceID || target.Omnidirectional))
       positional = Positional.Any;
   ```
   This is game-correct, not a bug: a mob turns to face its target instantly, so its rear cannot be
   reached by whoever holds aggro. Positionals only exist when something else has aggro (tank / Trust
   NPC), the mob is mid-cast, or with True North.

**Consequences:**
- Solo/farm/aggro-held content: positionals are unattainable by ANY movement system. Accept
  `AllowPositionalLoss` + True North there; don't burn effort on it.
- Tanked content (Trust, AutoDuty dungeons, parties): positionals are real, but BMR needs its preset
  cleared to chase them, and Daedalus-side vNav movement is what causes the stutter.

## Proposed design: preset-handoff mode ("BMR does everything")

New experimental toggle in Nav Control (default OFF), living next to Auto-Manage BMR AI:

1. **On enable (BMR loaded, in combat-relevant content):**
   - Remember the current preset: `AI.GetPreset` IPC (already subscribed in `BmrAiConfigService.CurrentAiPreset`).
   - Clear it: `AI.SetPreset(name)` IPC (`BossMod/Framework/IPCProvider.cs:509`) — passing a name that
     matches no preset resolves to `found = null` → `ai.SetAIPreset(null)` → **preset cleared**.
     e.g. `AI.SetPreset("")`.
   - Push movement-only config (existing `BmrAiConfigService` plumbing): `FollowTarget=true`,
     `MaxDistanceToTarget` by role, and the **live next-GCD positional** (`DesiredPositional=Rear/Flank`
     from `PositionalSnapshot.RequiredPositional` — plumbing already wired through
     `Plugin.UpdateBmrAiConfig`).
2. **Result:** BMR dodges (NavigationDecision always rasterizes forbidden zones), keeps range, and walks
   to rear/flank itself when feasible (its own :244 feasibility rule applies). **Daedalus never touches
   vNav in combat** — the pause/resume fight is structurally impossible. The MovementArbiter's sticky-
   steering yield already keeps max-melee maintenance quiet while BMR steers; consider hard-disabling
   Daedalus combat movement entirely while this mode is active for belt-and-braces.
3. **On disable / duty end / plugin unload:** restore the remembered preset via `AI.SetPreset(savedName)`.
4. **Re-assert loop:** each push interval (existing 0.25s rate cap), re-read `AI.GetPreset`; if AutoDuty
   re-applied its preset, re-clear (rate-limited, log via DebugLogService so the tug-of-war is visible if
   it happens).

## Open risks (the in-game unknowns)

- **Does AutoDuty fight over the preset?** If it re-applies aggressively (per-pull or per-frame), the
  re-assert loop becomes its own oscillation. First validation run should just watch the debug log for
  re-clear frequency. If it's per-pull, fine; if per-frame, this design needs an AutoDuty-side setting
  or IPC instead.
- **Between-pull navigation:** AutoDuty's own vNav paths (travel between packs) are foreign paths — the
  arbiter never touches them, and BMR yields to vNav anyway. Expect unaffected, verify.
- **Boss positional quality:** BMR walks to `GoalSingleTarget(positional)` at 2.6y preferred range; check
  it actually holds rear/flank through boss strafes well enough for Kazematoi/Aeolian uptime.
- **Preset restore on crash:** if Daedalus crashes mid-mode, the user's preset stays cleared (BMR AI
  reverts to plain follow — degraded but safe; it still dodges). Document in the toggle tooltip.

## Related current state (as of 2026-07-05, uncommitted)

- Boundary camping: implemented but default OFF (`NavConfig.EnableBoundaryCamping`); arc machinery +
  arbiter `MovementIntent` carve-out stay in the codebase; can be revisited or removed once this
  handoff mode is validated.
- Yield-to-BMR (default ON), GCD fast-requeue, chase destination-memory: keep regardless — they fix
  real bugs independent of who owns positionals.
- The `Hints.RecommendedPositional` IPC (BMR → us) is the *reverse* direction and reads BMR's own
  rotation hints — not useful while `ForbidActions=true`.

## Decision needed

Build the preset-handoff toggle? If yes: ~1 session (BmrAiConfigService extension + toggle + tests +
in-game validation run in AutoDuty w/ NIN watching preset re-clear frequency and Kazematoi positional
uptime).
