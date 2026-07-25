# Variant Dungeon Actions — Design Plan

> **Status: PLAN ONLY — no implementation until approved** (occult/meld precedent).
> Sources: `.cursor/rsr/RotationSolver.Basic/Rotations/Duties/VariantRotation.cs` +
> `RebornRotations/Duty/VariantDefault.cs`, `burn-reference/variant-dungeon-actions.md`,
> role table verified against the V&C wiki 2026-07-25.

---

## 1. How RSR does it (verified against source)

- **Territory gate:** `[DutyTerritory(1069, 1137, 1176, 1315, 1316)]` — Sil'dihn Subterrane,
  Mount Rokkon, Aloalo Island, The Merchant's Tale (+ Advanced).
- **Two-condition gate per action:** the instance applies a **"Set" status** for each of the
  (up to two) variant actions the player selected in the Dungeon Finder, AND the action must
  be **slotted on the duty bar**. RSR: `StatusNeed = [VariantXxxSet]` + `Info.IsOnSlot`.
- **Multiple action IDs per spell** — one per dungeon tier. RSR declares each explicitly
  (`VariantSpiritDartPvE`, `_33863`, `_46940`) and tries them slot-first.
- **Usage rules (VariantDefault):** Spirit Dart / Eagle Eye Shot on cooldown in the attack
  lane; Rampart in the defense lane (opt-in "spam on cooldown" config, otherwise paced by its
  applied Vulnerability Down); Cure in the single-heal GCD lane; Raise → Raise II in the raise
  lane; Ultimatum through the provoke lane.

### The data (all verified vs RSR resources)

| Action | IDs (tier variants) | Set status | Applies |
|--------|--------------------|------------|---------|
| Variant Cure | 29729 / 33862 / 46939 | 3565 | Rehabilitation regen (GCD heal) |
| Variant Ultimatum | 29730 | 3566 | AoE provoke, Enmity Up |
| Variant Raise / Raise II | 29731 / 29734 | 3567 | raise (Raise II = Criterion) |
| Variant Spirit Dart | 29732 / 33863 / 46940 | 3568 | Sustained Damage DoT (oGCD) |
| Variant Rampart | 29733 / 33864 / 46941 | 3569 | Vulnerability Down self-mit (oGCD) |
| Variant Eagle Eye Shot | 46942 | 4892 | big single-target hit (oGCD) |

### Role availability (what the Finder offers — wiki-verified)

| Action | Tank | Healer | Melee | Phys Ranged | Caster |
|--------|------|--------|-------|-------------|--------|
| Variant Cure | ✓ | — | ✓ | ✓ | ✓ |
| Variant Ultimatum | ✓ | ✓ | ✓ | ✓ | ✓ |
| Variant Raise | ✓ | — | ✓ | ✓ | ✓ |
| Variant Spirit Dart | ✓ | ✓ | — | — | — |
| Variant Rampart | — | ✓ | ✓ | ✓ | ✓ |
| Variant Eagle Eye Shot | ✓ | ✓ | ✓ | ✓ | ✓ |

- **Variant dungeons:** select **two**. **Criterion:** only Variant Raise II. **Criterion Savage:** none.
- Because the instance grants Set statuses only for the two selected actions, detection is
  driven by the statuses — we never need to know the selection UI state.

---

## 2. Daedalus design — reuse the occult architecture

The phantom layer already built everything this needs: pre/post hooks around every job's
modules in `BaseRotation`, an own `RotationScheduler`, morph-aware duty-bar matching,
Lumina-built `ActionDefinition`s, and the live Debug diagnostics. The variant layer is a
small sibling, not a new system.

```
Daedalus/
  Data/VariantActionData.cs         — territory set, action IDs + tier variants, set-status
                                      map, role table (for the config UI display)
  Rotation/Phantom/VariantActionLayer.cs — sibling of PhantomActionLayer, same hook sites
  Config/VariantConfig.cs           — toggles + thresholds (Configuration.Variant)
  Windows/Config/Shared/VariantSection.cs — sidebar BEHAVIOR ▸ "Variant"
```

- `RotationServices.VariantLayer` called from the SAME BaseRotation pre/post sites as the
  phantom layer (one line each). Inert outside the five territories.
- Per-action gate chain: territory → **Set status present** (this replaces occult's
  phantom-level gate) → duty-bar slot (tier-variant aware: each ID checked, slot picks the
  tier) → cooldown → range.
- No lockout-status list needed for oGCDs (they're plain weaves), but the Cure GCD pre-empt
  reuses the same "emergency heal ahead of job filler" path the phantom layer proved out.

### Behavior per action (defaults)

| Action | Band | Rule | Config |
|--------|------|------|--------|
| Variant Cure | emergency GCD (pre-empt) | self HP < threshold; INSTANT, 14,000 potency + regen (doubled under Rehabilitation) | threshold slider (default 60%) |
| Variant Rampart | self-mit oGCD | in combat, not already under its 60s Vulnerability Down (15s recast → can be permanent) | "spam on cooldown" toggle (default off, RSR parity) |
| Variant Spirit Dart | damage oGCD | **DoT maintenance, NOT on-cooldown** (2.5s recast would spam): apply when the current target lacks Sustained Damage (30s), hold while it ticks, reapply on expiry — AoE application covers nearby mobs (5y radius). FindEnemyNeedingDot house pattern | enable (default on) |
| Variant Eagle Eye Shot | damage oGCD | on cooldown at the current target (60s recast, potency scales with item level) | enable (default on) |
| Variant Raise / Raise II | recovery GCD | dead party member present; **8s hard cast on its OWN recast timer** — holds while moving | enable (default on) |
| Variant Ultimatum | utility oGCD | **default OFF** — multibox parties only want the MT provoking; 15s recast, 5y AoE provoke + 4s stun | enable toggle |

Tooltip-verified (2026-07-25 screenshots): Cure/Raise are Spells (GCDs), the rest are
Abilities (oGCDs) — matching the Lumina ActionCategory classification the layer already uses.

Damage actions here do NOT use the occult burst-hold (they're low-impact weaves with their
own recasts; RSR fires them freely).

### Config UI — Settings ▸ BEHAVIOR ▸ "Variant"

- Master **Enable Variant Actions** toggle (default ON in zone).
- One group per action: enable/thresholds per the table above, plus a dim **role line**
  ("Selectable by: Tank, Healer…") so each toon's owner can see what that role can equip —
  and a live chip when the action's Set status is active this run ("SELECTED" vs dim
  "not selected").
- Reminder line: selection happens in the V&C Dungeon Finder ("Set Actions") before entering —
  the plugin can only use what was selected and slotted.

### Debug

The Debug ▸ Occult tab becomes **Debug ▸ Duty**: existing occult block unchanged, plus a
variant block (territory, active Set statuses, slotted IDs with tier resolution, layer
last-event line — same live diagnostics style that made the occult field checks one-shot).

---

## 3. Phases

| Phase | Scope | Verify |
|-------|-------|--------|
| **1 — Detection + config** | VariantActionData, Set-status + slot reads in the Debug Duty tab, VariantConfig + Variant sidebar section with role table. Nothing fires. | Enter Sil'dihn with two actions selected: correct Set statuses + slotted tier IDs shown; config persists |
| **2 — Executor** | VariantActionLayer wired into the BaseRotation hooks: Cure pre-empt, Rampart, Spirit Dart / Eagle Eye Shot, Raise, Ultimatum (off) | Field run: Spirit Dart weaving on cooldown, Cure firing under the threshold, no main-rotation regressions outside the territories |

House rules apply: ≥4 regression tests per phase (pure rules extracted like PhantomBandRules),
both build configs, full suite green.

## 4. Open items for Phase 1
- Exact tier→ID mapping (which of the three Cure/Dart/Rampart IDs each dungeon uses) —
  resolved automatically by the slot read; the Debug tab will display which ID is live.
- Rehabilitation / Vulnerability Down variant status IDs for the "already applied" gates
  (RSR uses 3367 / 3360 — verify via the Debug tab in the field).
- Merchant's Tale (1315/1316) uses the newest ID block (46939-46942) — confirm in field.
