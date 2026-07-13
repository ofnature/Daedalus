# FC Company Chest — "Entrust All" Button (design plan)

> Status: PLAN ONLY (2026-07-12), feasibility verified against working prior art. Execution-ready
> for a cold-start session. Decision pending: which plugin hosts it (§6).

## 1. Feasibility — YES, with a proven mechanism

Prior art: **QuickTransfer** (github.com/Knack117/QuickTransfer) ships shift-click deposit into
the open FC chest today. Its mechanism (read from source, `QuickTransfer.cs`
`TryCompanyChestMoveItem`):

- The move is `RaptureAtkModule.Instance()->HandleItemMove(ret, values, 4)` with four AtkValues:
  `[srcInventoryType, srcSlot, dstInventoryType, dstSlot]` — **raw `InventoryType` values**
  (`Inventory1 = 0` … `Inventory4 = 3`, `FreeCompanyPage1 = 20000` … `FreeCompanyPage5 = 20004`),
  NOT container ids. This is the same path the game's own drag-drop uses — server-authoritative,
  no packet forgery.
- Preconditions: the **FreeCompanyChest addon must be visible** (open chest session;
  `GameGui.GetAddonByName("FreeCompanyChest", i)` for i=1..6), and the destination page must be
  a page the FC rank can deposit to (server refuses otherwise).
- The chest's **selected tab** can be read (QuickTransfer scans visible `FreeCompanyPageX`
  drag-drop payloads) and **switched programmatically** (addon callback param 1..5 = Items tab
  1..5 — QuickTransfer uses this for its organize feature).
- Partial-stack moves pop an **InputNumeric** dialog (QuickTransfer keeps AtkValue buffers alive
  8s and auto-confirms). Whole-stack moves to empty slots don't prompt.
- QuickTransfer paces transfers at ~200ms.
- Gil and crystals are separate containers (`FreeCompanyGil`, crystal pages) — **out of scope**;
  the item compartments are `FreeCompanyPage1..5` only.

## 2. Feature definition

An **"Entrust All"** button visible while the FC chest is open, depositing inventory items per a
policy, whole pull in one click. Modes (config, v1 ships the first two):

1. **Mirror mode (default)** — deposit every inventory stack whose itemId ALREADY EXISTS on the
   current chest tab (top up existing stacks, then empty slots). Safest: the chest's contents
   ARE the whitelist; you can never deposit something the FC doesn't already store.
2. **Whitelist mode** — deposit items on a configured list (item ids; UI adds by name search or
   "add what I'm holding"). The farm-mode tie-in: farmed mats list.
3. (v2) **Filter mode** — category filters (crafting mats / gear excluded / etc.).

Hard exclusions in every mode: equipped/armoury never touched (only `Inventory1..4` are
sources), untradeable/unique/collectable/spiritbound items skipped (Lumina `Item` sheet flags;
the server would refuse anyway — skipping avoids error spam), items in the gearset? (gear is
excluded by the tradeable filter in practice; v1 also skips anything with materia/spiritbond>0).

## 3. Architecture (Daedalus conventions)

```
Services/FcChest/
    FcChestDepositPlanner.cs   — PURE: (inventory snapshot, chest-page snapshot, policy)
                                 → ordered List<DepositMove>{srcType, srcSlot, dstType, dstSlot,
                                 expectedItemId, expectedQty, isMerge}
                                 Merge-into-existing-stacks FIRST (only merges that fit WHOLLY,
                                 so no InputNumeric ever fires), then whole stacks → empty slots.
                                 Fully unit-testable — this is where the tests live.
    FcChestDepositService.cs   — state machine driven per framework tick:
                                 Idle → Snapshot/Plan → Execute (ONE move per tick, ≥250ms pacing)
                                 → VerifyMove (re-read src+dst slots via InventoryManager; retry
                                 once, abort the run after 3 consecutive failures = permissions/
                                 full/closed) → NextMove → Done(report "Entrusted N stacks").
                                 Aborts instantly if the FreeCompanyChest addon closes, on zone
                                 change, or in combat. Snapshot via
                                 InventoryManager.Instance()->GetInventoryContainer(type).
    FcChestUi.cs               — ImGui overlay window pinned next to the open FreeCompanyChest
                                 addon (read addon X/Y/scale each frame; the standard overlay
                                 pattern). Button + mode dropdown + live progress line +
                                 last-run report. Disabled while a run is active or in combat.
```

- v1 targets the **currently selected tab only** (read it the QuickTransfer way); if the tab
  fills, stop with "tab full — N stacks left". v2 adds tab-advance via the addon callback
  (param 1..5) with a settle delay after switching.
- The `HandleItemMove` invoke gets the same treatment as QuickTransfer: AtkValue[4] alloc, call,
  free; wrap in try/catch; log retInt. No InputNumeric handling needed in v1 because the planner
  only emits whole-stack and fits-wholly moves.
- Config: `FcChestConfig { Mode, WhitelistItemIds, PaceMs (250, min 200), MaxMovesPerRun (140) }`.

## 4. Risks / open questions

- **HandleItemMove signature drift** — it's a ClientStructs member on RaptureAtkModule; smoke
  test after Dalamud bumps (same policy as other CS reads). Verify it exists in the pinned SDK
  before starting (string-scan FFXIVClientStructs.dll for `HandleItemMove` — 2 min).
- **Server pacing** — 200-250ms per move is field-proven by QuickTransfer; a full 140-slot dump
  is ~35s worst case. Show progress; allow cancel.
- **Permissions** — per-tab FC rank rights are server-side; the verify step converts silent
  refusals into a clean abort message ("no deposit rights on this tab?").
- **Multibox angle (later)** — a `charon.entrust` relay command could trigger every boxed toon's
  entrust run while their chests are open; out of scope for v1.
- **Withdraw-all** is the same machinery reversed — not planned, but the planner is direction-
  agnostic if it's ever wanted.

## 5. Tests (planner-level, min ~8)

Merge-before-empty ordering; wholly-fitting merges only (never emits a partial split); skips
untradeable/unique/spiritbound; mirror mode only matches current-tab item ids; whitelist mode
ignores non-listed; stops when the page is full and reports remainder; source restricted to
Inventory1-4; move-count cap respected.

## 6. Open decision — where does it live?

- **Option A: Daedalus** (Services/FcChest + overlay window). Pro: farm-mode synergy (later:
  auto-entrust when inventory fills during a farm run), all infra (config, overlay patterns,
  tests) exists. Con: Daedalus is a rotation plugin; this is pure QoL.
- **Option B: Charon** — it's the QoL companion; fits its identity. Con: Charon has no test
  suite / overlay infra comparable to Daedalus.
- **Option C: standalone plugin** (repo pattern exists — SealBreaker/Caduceus): cleanest
  identity, most setup overhead.

Recommendation: **A (Daedalus)** for v1 — the farm-mode auto-entrust follow-up is the real
payoff and it belongs to the farm loop. Revisit extraction later if it grows.

## 7. Build order

1. Verify `RaptureAtkModule.HandleItemMove` exists in the pinned ClientStructs (string scan).
2. `FcChestDepositPlanner` + full test suite (pure, no game deps).
3. `FcChestDepositService` state machine + addon-open/tab detection.
4. Overlay button UI + config section + progress reporting.
5. In-game validation: mirror mode on a junk tab first; verify pacing, abort-on-close,
   permission-refusal abort, full-tab stop. Then whitelist mode.
6. (v2) tab-advance, farm-loop auto-entrust hook, relay trigger for multibox.
