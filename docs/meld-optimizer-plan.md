# Meld Optimizer Window — Implementation Plan

> Status: PLANNING — no implementation until plan + mockup approved.
> Mockup: `docs/ui-mockups/meld-optimizer.html` (open in any browser).

## 1. Architecture

New standalone window + a `Services/Gear/` cluster. Everything below the window is either a
framework-thread reader (unsafe ClientStructs) or a **pure, unit-testable** component — the same
split the rotation stack uses (readers feed snapshots; logic never touches game memory).

```
Daedalus/Windows/MeldOptimizerWindow.cs          ImGui window: paperdoll canvas (DrawList), panels,
                                                 hover plumbing, optimize button + results overlay
Daedalus/Services/Gear/GearSnapshotService.cs    Equipped items + materia reader → GearSnapshot (cached)
Daedalus/Services/Gear/StatCapService.cs         Per-piece per-stat caps from Lumina (Item/BaseParam/ItemLevel)
Daedalus/Services/Gear/GearStatAggregator.cs     PURE: totals across gear + melds, overcap clipping
Daedalus/Services/Gear/MeldSweepOptimizer.cs     PURE: sweep + DPS ranking (runs on background Task)
Daedalus/Data/BalancePriorities.cs               Per-job stat priority order + relevance (data table)
Daedalus/Data/GcdBreakpoints.cs                  PURE: SkS/SpS → GCD formula + tier table (level 100)
Daedalus/Models/Gear/*.cs                        GearPiece, MateriaMeld, StatTotals, MeldPlan, MeldRecommendation
```

### Models (immutable snapshot types)

```
GearSlotId    enum: MainHand, OffHand, Head, Body, Hands, Legs, Feet, Ears, Neck, Wrists, RingL, RingR
GearPiece     { GearSlotId Slot; uint ItemId; string Name; int Ilvl;
                Dictionary<StatId,int> BaseStats;
                MateriaMeld[] Melds;              // current melds, in socket order
                int GuaranteedSockets;            // sweepable (grade XII, +54)
                int OvermeldSockets;              // sockets 4/5 on pentameld pieces — FIXED floor
                Dictionary<StatId,int> Caps; }    // per-stat cap for this piece
MateriaMeld   { StatId Stat; int Value; bool IsFixedOvermeld; bool Overcapped; }
GearSnapshot  { GearPiece[] Pieces; byte GenderId; uint JobId; DateTime CapturedUtc; }
MeldPlan      { per-piece per-socket StatId assignment; StatTotals Totals; float DpsDelta; }
```

## 2. Data reads (all new)

### 2.1 Equipped gear + materia — `GearSnapshotService`
- `InventoryManager.Instance()->GetInventoryContainer(InventoryType.EquippedItems)`, 13 slots.
  **Lift the proven pattern from SealBreaker** (`FarmController.GetAverageEquippedItemLevel`,
  D:\Dev\SealBreaker .. FarmController.cs:4191): skip slot 5 (belt, removed from game), skip
  OffHand when the mainhand's `EquipSlotCategory.OffHand != 0` (two-hander), `GetRowOrDefault`
  everywhere, whole read wrapped in try/catch (zone-transition crash pattern from CLAUDE.md —
  never read during transitions without null guards).
- Materia per socket: `InventoryItem.Materia[5]` (materia type row) + `MateriaGrades[5]` (grade
  index). Lumina `Materia` sheet maps (row, grade column) → `BaseParam` + value.
  **Current BiS materia model (confirmed in-game 2026-07-22):** grade XII = +54 per combat
  substat; grade XI = +18. A pentamelded piece is **3× XII + 2× XI** — the guaranteed sockets
  plus the first overmeld take XII; overmeld sockets 4/5 only take XI. Therefore the sweep
  covers **all XII sockets (3 on pentameld pieces, otherwise the guaranteed count)**; the two
  XI sockets are tagged `IsFixedOvermeld` and contribute a fixed +18 floor each, never swept.
- Base stats: Lumina `Item` sheet `BaseParam[]`/`BaseParamValue[]` arrays, PLUS the
  `BaseParamSpecial[]`/`BaseParamValueSpecial[]` HQ deltas when the inventory item carries the
  HQ flag. (Original assumption "endgame gear is never HQ" was field-disproven 2026-07-22 by an
  HQ crafted-then-augmented ring — its +21 Crit/+15 Ten HQ bonus was the Character-window drift.)
- Gender for silhouette: `IPlayerCharacter.Customize[(int)CustomizeIndex.Gender]` (0 = male,
  1 = female). Job comes from existing Daedalus job state (already wired).
- Cadence: refresh on window open, then every 2s while the window is open, plus manual refresh
  button. Reads happen on the framework thread only; the window renders the last snapshot.

### 2.2 Per-piece stat caps — `StatCapService`
The game's cap for substat S on piece P is derived from three sheets (the standard
Ariyala/xivgear formula):

```
cap(S, P) = round( ItemLevel[P.ilvl].{S column} × BaseParam[S].{slot% column for P's EquipSlotCategory} / 1000 )
```

- `ItemLevel` sheet: per-ilvl maximum for each BaseParam.
- `BaseParam` sheet: per-equip-slot percentage columns (1HWpn%, OH%, Head%, Chest%, ... Ring%).
- Cache per (ilvl, slotCategory, stat) — tiny.
- **Validation tests**: hardcode 4-6 known current-tier pieces (e.g. ilvl 760 body: Crit cap 551 —
  values verified in-game at implementation time, live data outranks any doc per the project's
  game-data rule) and assert the formula reproduces them. This is the highest-risk math in the
  feature; it gets the densest tests.

## 3. Data flow

```
framework tick (window open)
  └─ GearSnapshotService.Refresh()  ── unsafe reads → GearSnapshot (immutable)
        ├─ StatCapService.DecorateCaps(snapshot)      (cached sheet math)
        └─ GearStatAggregator.Totals(snapshot)        (pure)
              └─ MeldOptimizerWindow.Draw()
                    ├─ paperdoll + slot boxes + tooltips   (reads snapshot only)
                    ├─ aggregate stat table                (totals + per-stat overcap flags)
                    ├─ sidebar: BalancePriorities[job] + GcdBreakpoints.Tiers(totals.SkS|SpS)
                    └─ [Optimize Melds] ──► Task.Run(MeldSweepOptimizer.Sweep(snapshotClone))
                                                └─ ranked MeldPlan[] ──► marshal to UI thread
                                                      └─ overlay: piece highlights + diff tooltips
```

No service holds mutable state the window reads except the published snapshot reference +
published results reference (both swapped atomically — the static-backed lesson applies:
anything transient the UI reads must be a stable published copy, not a live field).

## 4. UI layout (mirrors the mockup — v2 after feedback)

Window default ~980×720, `DaedalusTheme` gold/dark identity throughout. Single-column flow:

```
┌──────────────────────────────────────────────────────────────────────┐
│  BALANCE PRIORITY BANNER — job icon/name · priority chips · note     │
├──────────────────────────────────────────────────────────────────────┤
│  PAPERDOLL — FULL WIDTH, scales with the window                      │
│   light bg rect · silhouette centered (male/female proportions)      │
│   L column (window left edge): Weapon Head Body Hands Legs Feet      │
│   R column (window right edge): Offhand Ears Neck Wrists R1 R2       │
│   anchors → AddLine leaders → slot boxes; hover → tooltip            │
│   (optimize overlay: gold outline on suboptimal pieces)              │
├───────────────────────────────┬──────────────────────────────────────┤
│  GCD BREAKPOINTS              │  [Optimize Melds] + ranked results   │
│   tier list, current value,   │   plans + ΔDPS, spinner while        │
│   worth-it verdict            │   sweeping                           │
├───────────────────────────────┴──────────────────────────────────────┤
│  AGGREGATE STATS (LAST)                                              │
│   stat | total | derived % | status — derived column uses the        │
│   characterstatus-refined formulas (see 5b)                          │
└──────────────────────────────────────────────────────────────────────┘
```

Scaling rule: slot-box columns pin to the window's left/right edges; the silhouette and its
anchors are computed from the canvas center in pixels each frame (no fixed aspect), so widening
the window stretches the leader lines, not the figure.

- Paperdoll primitives only via `ImGui.GetWindowDrawList()` (`AddRectFilled`, `AddCircleFilled`,
  `AddLine`, `AddRect` for highlights). Anchor positions are a normalized (0..1) coordinate table
  per gender, scaled to the canvas rect — no absolute pixels, so the window is resizable.
- Slot boxes are `InvisibleButton` hit areas → `IsItemHovered()` → tooltip. Tooltip sections:
  name, ilvl, base stats, melds (each `socket N: +54 Crit — OVERCAP 12 wasted` in red when
  clipped), per-stat caps line.
- Hover reads follow the existing rule: gate on `IsOpen && DrawConditions()`.

## 5. Balance priorities + GCD breakpoints (data-driven)

- `BalancePriorities`: static table per job — ordered stat priority, relevant-stat set, and a
  short note (e.g. SAM: `Crit > Det > DH`, SkS flagged "hold 420 unless running a speed set").
  Data entered from The Balance guides at implementation time; each entry carries a
  `// verified YYYY-MM-DD` tag so staleness is auditable (game-data rule 16).
- `GcdBreakpoints`: level-100 formula
  `GCD = floor(2500 × (1000 − floor(130 × (speed − 420) / 2780)) / 1000) / 1000` (ms→s),
  exposed as `TierFor(speed)`, `NextTier(speed)`, `PrevTier(speed)`, `PointsToNextTier(speed)`.
  Tier table unit-tested against known anchors (420 → 2.50, first 2.49 tier, etc. — asserted
  against in-game values at implementation time).

### 5b. Stat → % conversion — port from characterstatus-refined
[Kouzukii/ffxiv-characterstatus-refined](https://github.com/Kouzukii/ffxiv-characterstatus-refined)
(the plugin that replaces the Character window with derived percentages — crit chance/damage,
DH rate, det bonus, speed %/GCD) already carries the **exact level-modifier tables and
conversion formulas** for every substat. Port its coefficient math (with attribution + license
check at implementation time) into one `StatConversions` pure class:
`CritChance(crit)`, `CritDamage(crit)`, `DhRate(dh)`, `DetBonus(det)`, `SpeedBonus(spd)`,
`GcdSeconds(spd, baseRecast)`. This one class then feeds THREE consumers so they can never
disagree: the aggregate panel's derived column, the GCD breakpoint sidebar, and the optimizer's
DPS multiplier model. Unit tests assert against in-game Character-window values (e.g. the
screenshot set: Crit 1576 → 17.3% / 152.3%, Det 1352 → +7.0%, DH 1076 → 19.5%, SkS 803 →
+2.7%) so the port is provably faithful.
- Sidebar verdict line: "next tier needs +N SkS = M melds — costs M×54 Crit ≈ −0.4% DPS → not
  worth" — computed with the same DPS model the optimizer uses, so the two never disagree.

## 6. Meld sweep optimizer

### Search space
- Sweepable slots: `GuaranteedSockets` on every piece (all grade XII, +54). `OvermeldSockets`
  (4/5 on pentameld pieces) are a fixed stat floor — added to totals, never reassigned.
- Candidate stats per slot: the job's relevant substat set (typically 3-4: Crit/DH/Det/SkS-or-SpS;
  never main stat — main-stat materia is capped trivially and out of scope v1).
- Per piece: enumerate distributions of k sockets over s stats **that respect this piece's caps**
  (a distribution wasting >0 points to cap is kept only if no non-wasting distribution exists —
  dominance pruning). k ≤ 5, s ≤ 4 → ≤ 56 raw per piece, a handful after pruning.
- Global: DP over pieces on the stat-total vector. Totals are bounded and coarse (steps of 54, or
  cap remainders), so memoizing on the running total tuple keeps the space small; worst case is
  low-millions of states → milliseconds in practice. Runs on `Task.Run` against the immutable
  snapshot; UI shows a spinner and never blocks the draw thread.

### DPS model (ranking function)
Standard multiplicative substat model (the same math every community sheet uses):

```
mult(totals) = critMult(crit) × dhMult(dh) × detMult(det) × speedMult(sks|sps, job)  [× dot-weighting where relevant]
DpsDelta% = mult(candidate) / mult(current) − 1
```

- All rate/bonus math comes from the shared `StatConversions` class (section 5b — the
  characterstatus-refined port), so the optimizer, sidebar, and aggregate panel share one
  source of truth with a `// verified` tag and unit tests.
- SkS/SpS contributes only at tier boundaries for GCD jobs (the formula's floor), plus DoT/auto
  scaling where the job cares — the tier table feeds this directly.
- Output: top N (default 3) distinct plans, ranked by ΔDPS, each with per-piece per-socket
  assignments and a one-line summary ("+2 Crit −2 Det on Body/Legs, +0.31%").

### Recommendation overlay
- Pieces whose current melds differ from plan #1 get a gold `AddRect` outline on the paperdoll +
  a "suboptimal" chip on their slot box.
- Hovering shows current vs recommended per socket (arrows: `socket 2: DH → Crit`).
- No auto-melding in v1 — display only. (Auto-meld via agent interaction is a possible later
  phase; out of scope.)

## 7. Implementation phases

| Phase | Deliverable | Tests (minimum) | Exit criteria |
|---|---|---|---|
| 1 | Gear/materia/caps reads (`GearSnapshotService`, `StatCapService`, models) | Cap formula vs known pieces; materia grade→stat mapping; snapshot decoration | `/daedalus dumpgear` debug command prints full gear + melds + caps for the equipped set |
| 2 | Paperdoll UI (silhouette, anchors, lines, boxes, tooltips, gender variants) | Anchor-table normalization math | Window shows live gear on the figure; tooltips complete; resizing keeps layout |
| 3 | Aggregate stat panel (`GearStatAggregator` + `StatConversions` port) | Totals math; overcap clipping; relevance dimming; conversion formulas vs Character-window screenshots | Totals AND derived % match the in-game Character window for 2+ jobs |
| 4 | Sidebar (`BalancePriorities`, `GcdBreakpoints`) | Tier formula anchors; priority table integrity (all 20 jobs present) | SAM shows Crit>Det>DH with SkS dimmed; tier list highlights current value correctly |
| 5 | Optimizer + overlay (`MeldSweepOptimizer`) | Known-optimum synthetic sets; cap respect; overmeld floor fixed; tier-crossing flag; dominance pruning correctness | Optimize returns ranked plans < 1s; overlay + diff tooltips render; ΔDPS plausible vs a community sheet spot-check |

Each phase lands as its own commit(s), full suite green both configs, changelog entry only when
the window first ships user-visible (end of phase 2, then notable additions).

## 8. Conventions & risks

- **Never delete code** — superseded blocks are commented out with bracketed tags per project
  convention, not removed.
- **Zone-transition reads**: every unsafe read null-guarded and try/caught (known crash pattern).
- **Sheet drift**: cap formula and GCD/DPS coefficients re-validated each patch (rule 16 — live
  game data outranks snapshots); `// verified` date tags make stale entries greppable.
- **Perf**: snapshot reads are O(13) tiny; sweep is off-thread and memoized; window draw does no
  allocation-heavy work per frame (reuse cached strings where practical).
- **Scope guards**: no auto-meld, no gear *swap* suggestions (melds only), no HQ handling, BLU
  excluded (no meldable savage context), main-stat materia out of scope v1.
