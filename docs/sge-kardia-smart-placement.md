# SGE Kardia — chain-cast-on-tank-leave fix + smart low-HP placement

*Status: **IMPLEMENTED 2026-07-07** (same session as the plan, Fable). Both problems fixed as designed, plus one requirement added during implementation: the no-tank fallback is **highest-DPS ally (live parser rank, party-order pre-pull) → self**, not lowest-HP. Key deltas from the plan: the latch invalidation uses a 3s absence grace (immediate on death — Kardion strips); the latch kept its public names (`ConfirmTankKardion`/`IsTankKardionLatched`) but is semantically a BEARER latch now (docs updated, `OnKardiaDispatched` confirms any target); a cross-cutting "inference yields to a tracked bearer" guard was needed in four places (`TankHasKardion`, `TryFindKardionBearer` fallbacks, `ShouldBlockKardiaRecast` trust-inference branch, `PrimeTankKardionLatch`) so post-swap return-home casts aren't vetoed by "Kardion must be on the tank" inference; swaps bypass `ShouldSuppressKardiaRecast` (which is bearer-keyed by design) and only check `IsKardionOnTarget` on the destination. `IDpsMeterService` threaded into `IAsclepiusContext` (factory auto-injects). +11 tests (latch lifecycle in KardiaManagerTests, KardiaModuleSmartSwapTests). NOT unit-testable (mock limits): DPS-role filter (ClassJob) and parser-rank matching (Name.TextValue) — needs the in-game validation pass below.*

*Original plan follows for reference.*

Codename: **Asclepius** (SGE). Files: `Daedalus/Rotation/AsclepiusCore/Modules/KardiaModule.cs`, `Daedalus/Services/Sage/KardiaManager.cs`, `Daedalus/Config/SageConfig.cs`.

---

## Problem 1 — Kardia chain-casts when the tank leaves the zone

### Symptom
When the tank leaves the instance/zone mid-session (party member leaves/DCs, or the anchored tank despawns), the Sage begins re-casting Kardia every available weave instead of parking Kardion on a sensible fallback target and going quiet.

### Root cause — the tank-Kardion latch is never invalidated when the tank disappears
The whole suppression system is built around a "tank latch": once Kardion is confirmed on the tank, `KardiaManager` sets `_tankKardionConfirmed = true` / `_confirmedTankEntityId = <tank entity>` and every recast gate short-circuits on it. The latch is only ever cleared by `ResetSession()` (duty exit / territory change) — see `KardiaManager.cs:176-184`. There is **no path that clears it when the tank leaves but the Sage stays in the zone.**

Consequences once the tank is gone (`FindTankInParty` / `FindTankAlly` returns `null`):

1. **`UpdateKardiaTarget` lies about placement.** The first branch (`KardiaManager.cs:105-112`) is `if (_tankKardionConfirmed) { _hasKardionPlaced = true; return; }`. So the manager keeps reporting "Kardion is placed" while it is actually on nobody — the confirmed entity no longer exists.

2. **Dispatch falls to a churning fallback target.** `ResolveKardiaDispatchTarget` returns `tank ?? FindKardiaTarget(context)` (`KardiaModule.cs:140`). With `tank == null`, `FindKardiaTarget` (`KardiaModule.cs:535-543`) returns the lowest-HP party member, or the Sage. In a live pull the lowest-HP member can change frame to frame.

3. **The tank-path suppression can't fire for these fallback targets.** Every strong suppression branch is keyed on `target.EntityId == _confirmedTankEntityId` (`ShouldBlockKardiaRecast` `KardiaManager.cs:295-314`, `IsTankKardionLatched` `:259-262`). The new fallback target is not the (dead) confirmed tank, so those branches all miss. Suppression then depends only on `MatchesTrackedTarget` + live status (`IsKardionOnTarget` `:352-359`) — which flickers or fails whenever the resolved target changes, or on Trust NPCs that omit the Kardion (2605) status from their list. Net effect: gate returns "don't block," Kardia fires, next frame the target churns again, gate misses again → **chain cast.**

4. **The cast never re-latches.** `OnKardiaDispatched` only calls `ConfirmTankKardion` when `target.EntityId == tank.EntityId` (`KardiaModule.cs:392-394`). With `tank == null` that is skipped, so even a "successful" fallback placement doesn't produce a stable latch to suppress the next frame.

### Why it's specifically "chain" and not a single stray cast
`RememberBearer` does set `_lastKnownKardiaTarget` after each cast, which *would* suppress via `MatchesTrackedTarget` — **if the target stayed the same.** The chain persists because (a) the fallback target isn't stable (lowest-HP churn / tank-detection flicker between `null` and a re-resolved object), and (b) the stale `_tankKardionConfirmed` latch keeps `UpdateKardiaTarget` short-circuiting so the manager never rebuilds a clean truth about where Kardion actually is.

### Fix design (Problem 1)
Add explicit "tank absent / latch invalid" handling. Concretely:

- **Invalidate the latch when the confirmed tank entity is no longer resolvable.** In `UpdateKardiaTarget`, before the `if (_tankKardionConfirmed)` short-circuit, verify `_confirmedTankEntityId` still resolves to a live ally (`_objectTable.SearchByEntityId` / party scan). If it doesn't, drop the latch (`_tankKardionConfirmed = false; _confirmedTankEntityId = 0`) and fall through to real detection. This is the single highest-value change — it stops the manager from asserting a placement that no longer exists.
- **Stabilize the fallback target.** When `tank == null`, don't re-pick lowest-HP every frame. Once Kardion is confirmed on a fallback ally, latch *that* ally (generalize the latch from "tank entity" to "current bearer entity") and suppress recasts on it exactly like the tank, until it either dies/leaves or a swap rule (Problem 2) moves it.
- **Guard the dispatch when there is genuinely no valid ally.** If `FindKardiaTarget` can only return the Sage and we're not solo, prefer holding (debug `"No ally for Kardia"`) over spamming.
- Keep `ResetSession` on territory change as-is; this adds the *mid-zone* invalidation it was missing.

---

## Problem 2 — smart Kardion placement (swap to injured players, then return to tank)

### Current state: the feature is scaffolded but dead
`KardiaManager.ShouldSwapKardia(currentHp%, newHp%, threshold, newTargetIsTank)` (`KardiaManager.cs:414-433`) already encodes the intended rule:
- swap **to** a hurt non-tank when the current target is healthy and the new one is below threshold (with a 15% hysteresis gap to avoid thrash);
- swap **back to tank** when the off-tank target has recovered.

The config exists too: `SageConfig.KardiaSwapEnabled` (default true) and `KardiaSwapThreshold` (default 0.60), wired into the Settings UI (`SageSection.cs:44-50`).

**But `ShouldSwapKardia` is never called by the rotation.** The only swap logic actually in `KardiaModule.ResolveKardiaDispatchTarget` (`KardiaModule.cs:126-141`) is the *return-to-tank* case. There is no branch that moves Kardion **to** a low-HP ally. So today Kardion is a tank-only system in practice, and `ShouldSwapKardia` / `KardiaSwapThreshold` are orphaned.

### Fix design (Problem 2)
Wire the injured-ally swap into `ResolveKardiaDispatchTarget`, gated on `KardiaSwapEnabled && CanSwapKardia` (the 5s `SwapCooldown` already exists — respect it so we don't thrash and don't waste the swap):

1. Resolve the **current bearer** and its HP% (tank in the normal case).
2. Scan party for the **lowest-HP ally below `KardiaSwapThreshold`** (reuse `PartyHelper.FindLowestHpPartyMember`, then HP-gate it).
3. If `ShouldSwapKardia(currentHp%, candidateHp%, KardiaSwapThreshold, newTargetIsTank:false)` → return that ally as the desired target ("smart heal swap").
4. When that ally climbs back above threshold, the existing return-to-tank branch pulls Kardion home (already covered by `ShouldSwapKardia`'s `newTargetIsTank` path — just make sure it's evaluated each frame).
5. **Priority ordering:** an urgent low-HP swap should win over "return to tank"; both are gated by `CanSwapKardia` so only one swap per 5s. Consider a hard floor (e.g. < 30% HP) that ignores the 15% hysteresis for emergencies — decide during implementation.

Interaction with Problem 1: once the bearer latch is generalized from "tank entity" to "current bearer entity," the injured-ally swap slots in cleanly — a swap is just "move the latch to a new confirmed bearer."

### Open design questions (resolve before building)
- **Emergency override of the 5s swap cooldown?** RSR/most SGE guides say no — Kardia is passive supplemental healing, not a triage tool; direct oGCD heals (Druochole/Taurochole) handle spikes. Recommend: respect the cooldown, no override, and keep the threshold conservative (default 60% is already reasonable). Confirm against `.cursor/rules/burn-reference/sge-rotation.md` before implementing.
- **Multi-tank content:** which tank is "home"? Current `FindTank*` returns the first tank; fine for dungeon/Trust, revisit for raid.
- **Does moving Kardion off the tank ever cost a tank-death risk in dungeons?** In 4-man Trust the tank rarely spikes; low. Keep default ON but document the tradeoff in the config tooltip.

---

## Test plan (both problems)
Add to `Daedalus.Tests/Rotation/AsclepiusCore/Modules/KardiaModule*Tests.cs` and `Daedalus.Tests/Services/Sage/KardiaManagerTests.cs`:

**Problem 1 (regression):**
- Tank confirmed → tank entity removed from object table/party → `UpdateKardiaTarget` drops the latch and `_hasKardionPlaced` reflects reality.
- Tank gone + churning lowest-HP fallback → Kardia dispatched **at most once**, then suppressed (latched on the fallback bearer). Assert no repeat dispatch across N frames with shifting HP.
- No valid ally (solo-but-in-party edge) → holds, no cast.

**Problem 2 (feature):**
- Tank healthy, off-tank at 40% (threshold 60%) → desired target = off-tank; `ShouldSwapKardia` true.
- Off-tank recovers to 80% → desired target returns to tank.
- Within 5s of a swap → `CanSwapKardia` false → no second swap even if a lower target appears.
- Hysteresis: current 62%, candidate 55% → no swap (gap < 15%).
- `KardiaSwapEnabled == false` → never leaves tank.

Minimum 4 tests per module change (workflow rule). Build Debug **and** Release, full suite green before commit. User-facing → add a `CHANGELOG.md` entry.

---

## Summary
- **Bug:** the tank-Kardion latch is only cleared on zone change, never when the tank leaves mid-zone; the manager then asserts a phantom placement while dispatch churns over lowest-HP fallbacks that the tank-keyed suppression can't cover → chain cast. Fix by invalidating the latch when the confirmed bearer entity stops resolving, and by generalizing the latch to "current bearer" so fallbacks are stable.
- **Feature:** the injured-ally swap logic (`ShouldSwapKardia`) and its config already exist but are unwired — only return-to-tank is implemented. Wire the low-HP swap into `ResolveKardiaDispatchTarget`, respecting the existing 5s swap cooldown and hysteresis.
