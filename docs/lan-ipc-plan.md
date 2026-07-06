# LAN / Burst-Alignment IPC — Execution Plan

> **Audience: the implementing session (cold start).** Companion references:
> `.cursor/rules/burn-reference/ipc-burn-protocol.md` (8-toon burn sync protocol notes),
> CLAUDE.md "IPC Architecture" section, and `burn-reference/proteus-v2-v3-plan.md` §V3 (BLU
> payloads that ride this transport). The transport layer EXISTS and is committed — this plan is
> the remaining phases on top of it. Verify current code before trusting any file/line claim here.

## What exists today (shipped 8402253 + cb1338d, 2026-07-02/03)

`Daedalus/Services/Network/`:
- **LanMessage** — compact JSON envelope; SenderId `Name@World`, per-machine MachineId
  (`Environment.MachineName`, legacy GUID migration), version field, additive JSON fields only
  (e.g. `eh` EchoHeldMs) for cross-version tolerance.
- **LanCoordinator** — UDP broadcast 255.255.255.255 on `PartyCoordination.LanPort` (default
  47200), ReuseAddress (4 clients per box), background receive thread that only queues; bind
  failure → Status=Error + IPC-only fallback.
- **CoordinationBus** — framework-thread pump (Plugin.OnFrameworkUpdate); dedup (same-machine
  IpcMirror drop + (SenderId,Timestamp,Type) ring); mirrors all 14 `PartyCoordinationService`
  On*Ready events to LAN and routes inbound into the same HandleRemote* methods Dalamud IPC uses —
  **rotation modules never know the source**.
- Shipped behaviors: 2s heartbeats (job/HP/role + echo latency with dwell correction), roster
  (grey 5s / drop 30s), zone-in RoleAssignment with **deterministic slotting** (sort by SenderId →
  Tank1/2, Healer1/2, DPS1-4; identical on every machine, no negotiation), BurstReady/BurstFire
  with deterministic coordinator election (alphabetically-first fresh toon), BurstFire →
  `HandleRemoteBurstWindowStart` (opens the existing burst window state machine → every job's
  `GetBurstWindowState().IsActive` gate, zero per-job changes), healer-down detector, LanPartyWindow
  UI, settings in PartyCoordinationSection.
- **Spec'd non-goals (keep them)**: fight-timeline sync, mechanic coordination, Queen step LAN
  sync, TCP fallback.

## Phase 0 — two-machine live validation (BLOCKS everything; manual, no code)
1. **Windows Firewall inbound UDP 47200 rule on BOTH machines first** — nothing works without it.
2. Enable both, LanStatus peers > 0; same-machine grouping shows both local toons under
   "Machine 1 (Local)", Peers counts remote only.
3. Cross-machine latency reads single-digit-to-low-double ms (dwell correction cb1338d).
4. Zone both into the same dungeon → RoleAssignment exchange, identical slotting on both.
5. Dedup check: two toons on one machine + one remote — no duplicate handling of mirrored events.
Record results in the LAN memory; any failure here is a transport bug and preempts all phases below.

## Phase 1 — BurstReady triggers (choreography exists, nothing calls it)
- `bus.BroadcastBurstReady()` is the public API; the election + BurstFire consumer already work.
- Work: per-job "burst is ready" predicate that fires the broadcast. Do NOT hand-write 21
  definitions — add one virtual to the rotation base (`IsBurstReady`) defaulting to "2-minute raid
  buff off cooldown" per role (PLD FoF, WAR IR, NIN Kunai's/Trick+Suiton, SMN Searing Light,
  BLM Ley Lines?, healers = none/always-ready), overridden only where the default is wrong. The
  coordinator fires BurstFire when ALL rostered toons reported ready within a freshness window
  (already the election's job — verify).
- Tests: predicate defaults per role; staleness (a dead/desynced toon must not block the party —
  timeout → fire without it, config `BurstReadyTimeoutSeconds`).

## Phase 2 — countdown / pre-pull alignment (the CLAUDE.md backlog item)
- Purpose: trial/savage openers — every toon's pot + opener aligned to a shared T0.
- Protocol: coordinator broadcasts `CountdownStart { T0Utc, DutyContentId }` (source: the in-game
  countdown chat event on ANY toon, or a manual button). Consumers schedule their existing
  RSR-style `CountDownAction` hooks off T0: pre-pull pot at job-specific offset (SMN/BLM caster
  pots ~-2s, melee -1s, BLU Whistle -10s per the Moon Flute opener), ranged pull / Provoke / TBN
  at their offsets, opener sequence armed at T0.
- Clock skew: T0 as UTC + measured per-peer latency (heartbeat echo) is sufficient on LAN
  (single-digit ms); do NOT build NTP.
- Work items: (a) countdown chat/event detection → broadcast; (b) a small `PrePullScheduler`
  service (offset table per job, consumes T0, arms actions through the normal scheduler);
  (c) per-job opener sequences are the long tail — start with pot-only + "hold GCDs until T0",
  which is 90% of the sync value; scripted openers can land job-by-job later.
- Tests: scheduler math (offsets, already-past T0, cancel on countdown abort), no double-pot.

## Phase 3 — Phoenix Down execution (detection + signal already shipped)
- `bus.OnPhoenixDown`/`OnHealerDown` events exist; missing piece is ITEM execution:
  `UseItem` via `ActionManager` (ActionType.Item, itemId 4566 HQ/NQ handling) + inventory count
  probe + 1s cast + weakness-aware target pick (the dead healer first, else party order).
- Safety: only when ALL healers dead (the existing detector), rate-limited, config off by default
  until validated. Test the inventory probe against zone-transition null patterns.

## Phase 4 — enmity sharing / tank swap / add signals (CLAUDE.md message-type backlog)
- Smallest useful cut: `TankSwapRequest` (main tank's toon broadcasts at N buster stacks; co-tank
  consumer flips stance/Provoke via existing EnmityModule hooks) and `AddSpawned` (first toon to
  aggro a new pack broadcasts; tanks' add-pull logic consumes). Design against the existing
  deterministic-assignment style — no negotiation, sender decides.
- Defer until a real 4+ toon party runs regularly; Phase 1-2 deliver the actual burn value.

## Phase 5 — BLU party payloads
- Entirely specified in `burn-reference/proteus-v2-v3-plan.md` §V3 (DoT ownership, Moon Flute
  sync, Gobskin/Cactguard coordination, Final Sting, Coil assignments). Implement after Phase 1
  and BLU v2.

## Order & effort
Phase 0 (manual, 30 min with both PCs) → Phase 1 (small) → Phase 2 (medium; pot-only cut first)
→ Phase 5 with BLU v3 → Phase 3 (small) → Phase 4 (on demand).

## Cautions
- CoordinationBus pumps on the framework thread — consumers must never block; heavy work queues.
- Additive-only JSON on LanMessage (mixed plugin versions across machines are the norm mid-rollout).
- The dedup ring keys on (SenderId,Timestamp,Type) — new message types must set Timestamp.
- Do not enable LAN in Trust validation runs (CLAUDE.md rule).
