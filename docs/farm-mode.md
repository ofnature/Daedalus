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

## v2 (NEXT): GarlandTools autofill
- Item search box → async Garland fetch (cache JSON on disk, MLH-style) → list droppers (mob, level,
  zone) → one click adds mobs to the profile (name→NameId via Lumina BNpcName; ambiguous names: prefer
  rows that exist in the current territory's BNpc layout, else all matches).
- Wiki flag coords (rough) as a *suggested* anchor; user's feet remain ground truth.
- Maybe: warn when the selected item's droppers live in a different zone than the player.

## v3 (LATER): quality of life
- Mount between distant spots (Henchman pattern: mount action + vnav fly=false).
- Saved profile UI (config plumbing already exists in FarmConfig.SavedProfiles).
- Multi-zone routes via Lifestream IPC (user said no teleport-gil waste — make it opt-in).
- Rate stats: kills, items/hour, ETA in the Farm window; session summary on stop.
- Optional: HQ counting, retainer-aware "stop at N total across bags".

## Gotchas for whoever picks this up
- Rotations read `DutyConfigurationService.RotationConfiguration` (a COPY) — live flags must be
  process-wide statics (see `ExternalCombatOverrideState`), never Configuration instance members.
- `NamePlateIconId` (gold quest icon) is on GameObject @0x110 — used by Questionable flagged-hunt;
  NOT useful for farm mobs (ambient mobs are unflagged).
- AoE hit counting must not LoS-raycast; targeting must (see daedalus-job-rotation-patterns memory #11).
- Chat prints go through the service's Notify event → Plugin → chatGui (keeps ImGui/chat out of services).
