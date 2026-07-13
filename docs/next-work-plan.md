# Daedalus — Next-Work Plan (cold-start handoff)

> Written 2026-07-12, immediately after the v0.1.17 release. Audience: a fresh session (any
> model) picking up work with zero conversation context. Read `CLAUDE.md` first (conventions,
> build rules), then this file. The auto-memory ledgers under the harness memory dir
> (`daedalus-plans-ledger`, `daedalus-trust-validation-status`, `daedalus-lan-coordinator`,
> `daedalus-farm-mode`, `daedalus-release-process`) carry the same state with more history —
> when this file and a ledger disagree, verify against code and update both.

## Where things stand (v0.1.17, tag pushed 2026-07-11)

- **All 21 implemented jobs are Trust-validated except BLU** (never run in-game; NIN closed
  2026-07-11, DRG closed incl. quest milestones, SMN/BLM/DNC/SCH/RDM all closed earlier).
- v0.1.17 shipped five feature sets in one release: farm mounted travel, tank-swap debuff
  watch, the settings set-before-save fix, the full BLU wave (loadout support + freeze→shatter
  + Moon Flute trigger + role-loadout apply), and the Charon LAN items (roster vitals + relay).
  **None of the v0.1.17 features have an in-game validation pass yet.**
- Unified manifest (`repo.json` on main) serves four plugins: Daedalus 0.1.17.0,
  Charon 0.1.1.0, SealBreaker 1.1.0.0, Caduceus 0.1.0.0.

## A. Code work, ready now (no in-game dependency)

### A1. EnumCombo set-after-save audit — small, do first
The v0.1.17 fix (655cecb) repaired `ConfigUIHelpers.Toggle`: it saved BEFORE writing the new
value, so every checkbox propagated its OLD value to the rotation-facing config copy
(`DutyConfigurationService.RotationConfiguration`, refreshed inside `SaveConfiguration`) and
even persisted the old value to disk. The same trap still exists in every **EnumCombo caller**
that assigns the ref-returned value AFTER the call returns (EnumCombo saves internally):
`if (EnumCombo("X", ref v, ...)) { config.X = v; }` — the assignment lands after the save.
- Grep `EnumCombo(` across `Daedalus/Windows/Config/` (+ any other get/set-style helper callers).
- Fix pattern: either add `save()` after the assignment (done for BlueMageSection's Role combo)
  or refactor EnumCombo to take a setter like Toggle does (cleaner; touch all callers once).
- Rule to preserve: **the config value must be written before save() runs** — save is not just
  persistence, it is the propagation barrier to the rotation config copy.
- Baseline: both configs 0 errors, ~3889 tests green, no new warnings.

### A2. Charon-side consumers (repo `D:\Dev\Charon`, its ROADMAP #7/#8)
Daedalus-side is DONE (7ee73d4). Contract: `.cursor/rules/charon-lan-integration.md`
(gitignored, in this repo's working tree). Charon work:
- `DaedalusIpcClient` (tolerant parser): read the new roster fields `hp` (float 0–1, ~2s
  stale) and `entityId` (uint, 0 from old Daedalus) — extend-only, ignore-unknown parsing
  already in place.
- Subscribe `Daedalus.Relay.Message` (string channel, string json) and add a publish wrapper
  for `Daedalus.Relay.Publish`. Fail-open: call-gate failure = Daedalus absent → keep the
  observation-based fallbacks.
- Build Heal Watch (#8): healer alt heals fleet toons OUTSIDE its party from roster vitals —
  detect via roster `hp`, resolve the toon via `entityId` in the local object table, RE-CHECK
  live HP before casting. Prior art: Coppelia (github.com/McVaxius/Coppelia).
- Convert pillion seat assignment (#7) from observation-inferred to owner-authoritative
  messages over a `charon.pillion` relay channel; `/charon rally` broadcast likewise.
- When Charon releases: bump its `AssemblyVersion` in THIS repo's `repo.json` (manual, like
  2026-07-11's 0.1.1.0 bump) and verify the release asset URL returns 200.

### A3. NIN optional optimization (small)
Suiton+Trick on dying packs at pull end: gate `EvaluateNeedsSuiton` on the shared
`PackTtkEstimator` + a dying-target HP floor on the Trick push (the MCH complementary-pair
pattern, already used for the Trick hold itself — see HermesCore + trust ledger NIN entry).
Not a bug; only do it when asked or bundled with other NIN work.

### A4. SMN Topaz-interleave question (investigate before touching)
Titan phase interleaves Ruin III between Topaz Rites (~4.7s Topaz spacing vs ~3.0s optimal).
VERIFY Topaz Rite's real recast vs XIVAPI and how RSR's CanUse waits for within-GCD-window
recasts BEFORE changing anything — repo data says 2.5s, suspicion is 3.0s. Optimization, not
a correctness bug.

### A5. CLAUDE.md maintenance sweep (docs-only commit)
Stale items to scrub: "Pending Plugin: Caduceus" section (Caduceus is RELEASED at 0.1.0,
own repo ofnature/Caduceus); the job-status table (codename ledger in the trust memory is
authoritative — NIN/DRG/SMN/BLM/etc. all validated, MCH codename listed "in progress" is
long done); "repo.json also hosts Memoria" note (Memoria was scrubbed; it now hosts
Charon/SealBreaker/Caduceus).

## B. Blocked on in-game runs (user drives; code follow-ups likely)

### B1. BLU first run — TOP validation priority
The rotation now matches the user's actual 24-slot loadout. Checklist (full version in the
trust ledger BLU entry): Basic Instinct + Toad Oil at solo combat start; Bristle→Mortal Flame
(ONCE per target — chain-casting means status 3643 is wrong in practice; the 60s latch caps
the damage and Debug shows it); Bristle→Breath of Magic ~60s cadence; freeze→shatter on a 2+
pack (all damage held between Ram's Voice and Ultravibration; bosses → one attempt then
"pack immune"); Cold Fog only when being attacked, White Death spam after a hit; Surpanakha
4 charges strictly back-to-back; Feather Rain ground-placed; Moon Flute stays OFF until the
rest validates. Loadout apply: without mimicry = instant "Applied N/24" (already field-proven);
with mimicry = "Waiting: drop Aetheric Mimicry" → quick job flip → applies + auto-mimicry
recasts. **Game fact (verified 2026-07-11): Aetheric Mimicry cannot be cancelled
programmatically** — ExecuteStatusOff returns false, /statusoff silently no-ops despite
CanStatusOff=true; only a job change drops it. Do not re-attempt automation of the cancel.

### B2. Farm mounted travel validation (v0.1.17, 85e6e3d)
Checklist in `docs/farm-mode.md` §v4 + farm memory: mount fires on >40y legs only, dismount
lands before engage (no mounted attack fizzles), fly in attuned zones / ground in ARR zones,
combat mid-flight dismounts and fights, specific-mount-locked falls back with one warning.

### B3. Coordinated tank swap — auto path
Manual path live-validated 2026-07-08. Pending: auto-stack trigger in real buster content
(now debuff-watch-filtered, cc9ac69 — only real "damage taken increased" stacks count),
pre-mit observation, panic test (OT rips aggro without signal → MT reclaims once, no
oscillation).

### B4. SGE Kardia smart placement (v0.1.12) — real-party run
Tank-leaves-zone → one re-placement onto top-parse DPS then quiet; swap-and-return ≥5s apart;
no-tank fallback; solo unchanged.

### B5. LAN Phase 0 — two-machine live test (MANUAL; blocks the whole IPC ladder)
`docs/lan-ipc-plan.md`. Windows Firewall inbound UDP 47200 rule FIRST. Also new: validate the
plugin relay with two clients on one machine (publish on one box, sibling receives — the
loopback mirror is the transport). After Phase 0 passes, the ladder is: Phase 1 BurstReady
per-job triggers (base-class predicate) → Phase 2 countdown/pre-pull alignment (pot-only cut
first) → Phase 3 Phoenix Down execution (needs UseItem machinery) → Phase 4 tank-swap/add
signals (deferred until real 4+ parties) → Phase 5 = BLU v3.

## C. Bigger tracks (execution docs exist; start only when their blockers clear)

- **BLU v3 multi-BLU LAN** — `burn-reference/proteus-v2-v3-plan.md` (local, gitignored):
  V3.1 DoT ownership + capability-bitfield election → V3.2 Moon Flute sync → V3.6 co-op
  freeze→shatter → V3.3 Gobskin/Cactguard → V3.4 Final Sting rotation + V3.5 Coil per-duty
  assignments. Prereq: LAN Phase 0 (B5) + BLU first run (B1). All design decisions are
  already made in the doc — implement as written.
- **Farm v3 QoL** — `docs/farm-mode.md` (listed after v4; v4 shipped).
- **BMR positional handoff** — `docs/bmr-positional-handoff.md`: design written, AWAITING
  USER DECISION on the AI.SetPreset("") approach. Do not implement unprompted.
- **BLU spell-acquisition farm helper** (idea, no doc): wire the Missing window's learn
  sources into farm mode — "Farm this spell" button, stop-on-learned condition, and a
  hold-damage-until-the-mob-casts option (open-world BLU learns require witnessing the spell).
  Duty-sourced spells just list for AutoDuty runs. Was deprioritized when the user's loadout
  turned out sufficient; revisit when they want more of the spellbook.
- **FC chest "Entrust All" button** — `docs/fc-chest-entrust-plan.md` (feasibility VERIFIED
  against QuickTransfer's working RaptureAtkModule.HandleItemMove mechanism; pure planner +
  paced state machine + overlay button; host-plugin decision pending, recommendation Daedalus
  for the farm auto-entrust follow-up).

## Cross-cutting rules the next session must not relearn

- Build BOTH configs; full suite green (3889 as of v0.1.17); no new warnings; CHANGELOG only
  for player-facing changes, inside the LATEST block.
- Verify ids/recasts/statuses via XIVAPI/RSR BEFORE trusting memory or docs
  (`daedalus-check-rsr-early`); RSR checkout `.cursor/rsr/`, BMR `.cursor/bmr/`.
- Config-copy class: rotations read a COPY of Configuration (DutyConfigurationService);
  transient flags must be static-backed; UI helpers must set-before-save (A1).
- `IsActionReady` lies on recast-group GCDs mid-global — use `GetCooldownRemaining`.
- Debug builds auto-reload the plugin in-game; only commit/push when the user asks.
- Release flow + gotchas: CLAUDE.md "Versioning & Releases" + `daedalus-release-process`
  memory (API-payload method for release notes; tag-pinned links; verify via
  api.github.com, not the raw CDN).
