# Farm Mode — design & roadmap

> Reference doc for future sessions (written 2026-07-04, v1 implemented same day). Context: user
> farms overworld mob drops on multibox toons. Goal: "move to a spot, kill the right mobs, stay in
> the area until X of item Y is in the bag." Roam stays within one zone — no teleporting (gil).

## Key research findings (verified 2026-07-04)

- **Monster Loot Hunter has NO IPC** (cloned `danielbrenom/MonsterLootHunter`, zero call gates).
  It is a thin UI over two web sources:
  - **GarlandTools**: `https://www.garlandtools.org/db/doc/item/en/3/{itemId}.json` — machine-readable
    JSON; `item.drops` + `partials` give dropper mob names, levels, zones. The right source for v2.
  - **FFXIV Console Games Wiki**: `https://ffxiv.consolegameswiki.com/mediawiki/api.php?action=parse&page={item}&format=json`
    — HTML parse (MLH's `WikiParser.cs`); yields mob name / zone / level / map-flag coords. Fragile; use
    only if Garland lacks coords.
- Game data (Lumina) does NOT contain drop tables — server-side. External lookup is mandatory for autofill.
- **Mob identity = BNpcName row id (`NameId`)** — language-independent, stable. Capture UX: target the
  mob once → "Add current target". Name→NameId resolution (for web-sourced names) via Lumina BNpcName sheet.
- Overworld drops auto-enter the inventory (no loot window). Count via
  `InventoryManager.GetInventoryItemCount` (already wrapped by `DalamudInventoryProbe`).

## Reused Daedalus machinery (all field-validated in the automation-bridge arc, v0.1.2–v0.1.4)

| Need | Existing piece |
|---|---|
| Turn rotation on while farming | `ExternalCombatOverride` (process-wide static; rotations read a config COPY — never put live state on Configuration instance) |
| Open on passive mobs | Automation engage in `BaseRotation` (hard target + override ⇒ counts as combat; mounted guard) |
| Kill aggro first | `TargetingService.FindNearestAggroedEnemy` (game hater list + targeting-us) |
| Don't hit dummies | dummy guard in Plugin (override-scoped) |
| Move | `VNavService` (`PathfindAndMoveTo`, `PathfindAndMoveCloseTo`, `Stop`, `SnapToFloor`) |
| Count items | `DalamudInventoryProbe` |
| Status | gold `· Farm` source label (ExternalCombatOverrideState.Source) |

## v1 (SHIPPED — this doc's commit): manual profile, single zone, roam between spots

- `Daedalus/Config/FarmConfig.cs` — `ShowFarmButton` (Settings → General; Farm button on main HUD like
  LAN Party), defaults (leash 60y, respawn wait), and `SavedProfiles` (persistence WIRED but no UI —
  the working profile is deliberately session-only per user; add Save/Load UI later).
- `Daedalus/Services/Farm/FarmModeService.cs` — the driver. Runtime `FarmProfile` (item, target count,
  mob NameIds, patrol spots, leash). Holds override (Source "Farm") while running. Loop:
  aggroed-on-us first → profile mob within leash of the active spot → engage kills → inventory check →
  roam to next spot when nothing up for `RespawnWaitSeconds`. Approach distance by role (melee/tank
  ~2.5y, ranged/healer/caster ~18y). Stops on: count reached, death, zone change, user stop.
- `Daedalus/Services/Farm/FarmRoamPolicy.cs` — pure decision logic (unit-tested).
- `TargetingService.FindNearestEnemyByNameIds` — nearest valid enemy whose NameId is in the profile,
  optionally leashed to a spot.
- `Daedalus/Windows/FarmWindow.cs` — item search (Lumina), target count, live bag count, mob list
  (add-from-target), spot list (add current position), leash slider, Start/Stop + status.

### v1 caveats / known limits
- Working profile is temp (lost on plugin reload/logout) — by design for now.
- Same zone only; if the player leaves the zone, farming stops with a chat notice.
- Solo-positional movement is suppressed by design elsewhere; farm does its own approach via vNav.
- Patrol = user-placed spots (first spot is the anchor). No pathing intelligence between spawn clusters.

## v2: GarlandTools autofill — PARTIALLY SHIPPED (95469f0, same day)
- DONE: `GarlandDropSource` (async item-doc fetch, in-memory per-item cache, mob partials parsed,
  name→NameId via English Lumina BNpcName sheet) + "Find droppers" button in the Farm window.
- REMAINING: disk cache for lookups; zone display per dropper (Garland `obj.z` is their own
  location id — needs Garland's core location table or a wiki fallback); warn when droppers live
  in a different zone than the player; wiki flag coords as suggested anchor.

## v3 (LATER): quality of life
- Mount between distant spots (Henchman pattern: mount action + vnav fly=false).
- Saved profile UI (config plumbing already exists in FarmConfig.SavedProfiles).
- Multi-zone routes via Lifestream IPC (user said no teleport-gil waste — make it opt-in).
- Rate stats: kills, items/hour, ETA in the Farm window; session summary on stop.
- Optional: HQ counting, retainer-aware "stop at N total across bags".

## v4: mounted travel + 100y acquisition — IMPLEMENTED 2026-07-10 (awaiting in-game validation)

Shipped as specced below, with these implementation notes:
- `FarmMountPolicy` (pure decisions, 18 tests) + `FarmMountHelper` (game reads/casts: ICondition
  flags, ActionManager GeneralAction 9/23 + ActionType.Mount, PlayerState.IsMountUnlocked,
  TerritoryType.AetherCurrentCompFlgSet + PlayerState.IsAetherCurrentZoneComplete for flight).
- Travel hooks: `FarmModeService.HandleMountedTravel` runs first in both ApproachTarget (mob legs,
  edge distance, dismount at range) and Roam (spot legs, stays mounted on arrival — the next
  acquired mob triggers the dismount).
- Scan: player-range widened to max(ScanRadiusYalms, leash); the SPOT LEASH still applies — the
  wider funnel never pulls the toon outside the patrol area.
- Fly fallback is per-leg (`_flyFailedThisLeg`): one failed fly-path downgrades the leg to ground.
- Mount cast: stop-nav tick → cast tick → 3s await → one retry → 30s walk suppression.
- Travel settings live in the Farm window ("Travel" section) and persist in FarmConfig
  (unlike the session-only profile).

### Original spec (executed as written)

**Goal**: spot-to-spot travel happens mounted (fly where legal), and mob acquisition scans out to
~100y instead of the current engage-range scan, issuing movement to the nearest profile mob.

### 1. Mount handling (`FarmMountHelper`, new)
- Config: `FarmConfig.MountMode { Roulette, Specific }` (default Roulette) +
  `FarmConfig.SpecificMountId` (uint). UI: radio + a dropdown of UNLOCKED mounts
  (`PlayerState.Instance()->IsMountUnlocked(id)` over the Mount sheet, name + id — cache the list
  per session). Specific mount not unlocked → warn once, fall back to Roulette.
- Cast: Roulette = `UseAction(ActionType.GeneralAction, 9)` (Mount Roulette); Specific =
  `UseAction(ActionType.Mount, mountId)`. Gates before casting: not InCombat, not already mounted
  (`Condition[ConditionFlag.Mounted]`), zone allows mounts (`Condition` / TerritoryType — open
  world farm zones all do; sanity-gate anyway), standing still (mount cast is 1s and breaks on
  move — pause vNav for the cast, the arbiter's Stop path).
- Wait state: after UseAction, poll `ConditionFlag.Mounted` (timeout ~3s → retry once → give up
  and walk; never loop-cast).
- **Trigger**: distance to next destination (spot or acquired mob) > `MountDistanceThreshold`
  (config, default 40y). Below it, walk — mounting for a 20y hop is slower.
- **Dismount**: when within `DismountRange` (default ~15y) of the target mob, or before issuing
  the engage/first attack — `UseAction(ActionType.GeneralAction, 23)` (Dismount) while airborne
  descends first; poll un-mounted before combat starts (attacks fizzle mounted). Flying dismount:
  vNav land — pathfind with fly=false to a ground point near the mob, THEN dismount (avoid mid-air
  dismount fall damage / stuck-floating).
- **Flying**: pass `fly: true` to the arbiter's `PathfindAndMoveTo` when mounted AND the zone has
  flight unlocked (`PlayerState` aether-current completion for the territory — expose via a small
  probe; if unknown, try fly and fall back to ground on vNav pathfind failure). Ground mounts in
  no-fly zones: fly=false, everything else identical.

### 2. 100y acquisition scan
- Extend the FarmModeService mob scan radius: config `FarmConfig.ScanRadiusYalms` (default 50,
  max 100 — the object table reliably covers ~100y in open world; beyond that entries pop in/out).
- Match by the profile's existing mob identity (BNpcName/name match as v1 does), filter alive +
  targetable; **nearest first** (squared distance). Keep the v1 hater/claimed filtering.
- Acquired target beyond engage range → movement: mounted travel per §1 when > threshold, else
  vNav walk (all through the MovementArbiter as today — farm mode already routes through it).
- No match within radius → existing spot-roam behavior (this feature only widens the funnel).

### 3. Loop integration (state machine addition)
v1 loop gains two states: `Traveling(Mounted)` and `Dismounting`. Full cycle:
`ScanAtSpot(100y) → [match] MountIfFar → Travel → Dismount@15y → Engage/Kill (rotation owns
combat) → loot → Scan again → [no match] NextSpot (mounted if far)`. Combat interrupts any travel
state (aggro while mounted → dismount immediately, fight, resume). Inventory-full / target-count
reached → existing stop behavior unchanged.

### 4. Tests (pure logic; game reads mocked)
Mount-mode fallback (specific-not-unlocked → roulette), distance-threshold mount/walk decision,
dismount-range trigger, nearest-of-N selection with claimed/hater filtering at 100y, no-match →
spot-roam fallthrough, combat-interrupt state reset.

### Gotchas specific to this feature
- Mount/dismount are GeneralActions — the ActionStatus pre-gate patterns differ from combat
  actions; probe `GetActionStatus(ActionType.GeneralAction, ...)` before casting (mounted-state
  errors are silent otherwise).
- Never mount while a profile mob has aggro (combat check BEFORE the mount cast, not just at
  trigger evaluation — the gap between decision and cast bit the SMN demi timing; re-check at cast).
- vNav fly pathing needs the mesh loaded for the zone; `IsPathfindInProgress` can run long on
  first fly query — the arbiter's churn guards already rate-limit, don't add another layer.
- The 1s mount cast under the arbiter: issue Stop, cast, wait mounted, THEN pathfind — do not
  fight the movement pipeline mid-cast.

## Gotchas for whoever picks this up
- Rotations read `DutyConfigurationService.RotationConfiguration` (a COPY) — live flags must be
  process-wide statics (see `ExternalCombatOverrideState`), never Configuration instance members.
- `NamePlateIconId` (gold quest icon) is on GameObject @0x110 — used by Questionable flagged-hunt;
  NOT useful for farm mobs (ambient mobs are unflagged).
- AoE hit counting must not LoS-raycast; targeting must (see daedalus-job-rotation-patterns memory #11).
- Chat prints go through the service's Notify event → Plugin → chatGui (keeps ImGui/chat out of services).
