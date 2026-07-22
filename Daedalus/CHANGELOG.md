# Changelog

All notable changes to Daedalus will be documented in this file.

<!-- LATEST-START -->
## v0.1.38 — 2026-07-22

### New — Meld Optimizer (Analytics → Melding)
- A full gear and melding assistant, parked in the **Analytics window's new Melding tab** (also `/daedalus meld` for a standalone window — same panel, shared state). Top to bottom: your job's **Balance meld priority** as a banner (with the job emblem); a **paperdoll** of your equipped set — silhouette matches your character's gender, twelve slot boxes with hover tooltips showing base stats, every meld (grade, fixed-XI overmelds, overcap waste in red), and per-piece stat caps; **GCD breakpoint tiers** with your live value highlighted and an honest "is the next tier worth it" verdict; and an **aggregate stat table** showing character totals with the derived percentages (crit chance/damage, DH rate, det bonus, GCD + speed, MP/tick) exactly as the Character window computes them
- **Optimize Melds** sweeps every grade-XII socket across your job's priority stats — respecting each piece's per-stat caps and treating the two grade-XI overmeld sockets on pentamelded pieces as fixed — and returns ranked plans with a DPS delta vs your current melds. Plan #1 overlays the paperdoll: pieces needing changes get a gold outline and a "meld ↺" chip, and their tooltips list exactly which sockets to change (current → recommended). Speed stats are deliberately valued at zero for non-speed jobs (Balance rule: hold base tier — BLM is the exception and is treated speed-first)
- The gear snapshot refreshes every 2 seconds while the panel is visible, so re-melding updates everything live

<!-- LATEST-END -->
## v0.1.37 — 2026-07-22

### Fix — Bard: buffed Iron Jaws re-snapshot actually fires now (top-parse audit)
- Checked the rotations against rank-1 FFLogs parses from the current savage tier (patch 7.3, M5S): the one code-level gap found was Bard's buffed DoT re-snapshot. The rule "re-snapshot when Raging Strikes is up and DoTs are under 20s" **never triggered in openers** — DoTs applied at the pull still have ~30s left when Raging Strikes expires, so the buffed window came and went. It now re-snapshots in the **last seconds of Raging Strikes** (top-parse behavior: Iron Jaws ~17s into the fight with the full buff stack), carrying the +15% snapshot through ~40 more seconds of DoT ticks every burst window

### New — Casts no longer die to BossMod micro-steps (all jobs)
- The walk-in loop is fixed: a toon inside spell range but outside BossMod's stand distance would start a cast, BMR would take a step, the cast died — repeating the whole way in. Daedalus now **pauses BMR's AI movement while a cast bar is up** and releases it the instant the cast ends, so casts complete and BMR steps between them. Universal: every cast bar counts (caster/healer hardcasts, PCT motifs, SAM Iaijutsu, PLD Clemency) — jobs without cast bars are unaffected. **Dodging always wins**: if BMR expects damage or a zone activation before the cast would finish (plus a reaction buffer), the hold releases immediately and the cast is sacrificed. Guard rails: a watchdog force-releases any hold older than 8 seconds, the hold is released on plugin unload, and a "Hold BMR movement while casting" toggle (ON by default) plus a live status line sit in Nav Control under Movement Cadence

### Fix — Pictomancer: Smudge is a dash, and it now knows it
- Smudge turned out to be a **15-yalm forward dash**, not a sprint — and the old "weave it whenever moving" rule fired on every little BossMod position adjustment, dashing the toon past its destination (BMR then walked it back — much of the micro-stepping and cast-interruption churn) and, twice reported, straight off arena ledges. New dash guard: Smudge only fires on a **real navigation leg** (vnavmesh path running — BMR micro-steps and strafing can never trigger it), only when the toon is **moving the way it's facing** (no dashing at the boss while strafing under auto-face), and only after checking the **navmesh floor at the dash midpoint and landing** (off-mesh or a ledge drop = no dash — abysses are out) plus BossMod's hazard flags at both points. Without vnavmesh loaded, Smudge simply never auto-fires

### Fix — Paladin: Clemency no longer casts into movement (all-jobs cast-gate audit)
- A full audit of every job's cast-time actions against the shared move/cast gate (the same class of bug as the Samurai Midare fix) found exactly one hole: **Clemency** — a 1.5-second hard cast with no instant proc — had no gate, so a moving Paladin below the emergency threshold would start the cast, get it cancelled by movement, and retry every GCD. It now holds while moving (and before predicted raidwides/busters) like every other hard cast. Everything else checked clean: all caster/healer damage and heal casts, RPR's Communio/Harpe fallbacks, PCT's motifs and subtractive combo, and Smudge is confirmed wired (weaves automatically while moving, with the Swiftcast movement weave alongside)

## v0.1.36 — 2026-07-20

### Fix — Quest/hunt kill loops: one deliberate pull at a time
- The Questionable kill bridge already pulled a single mob and finished everything aggroed before pulling fresh — but the moment the last mob died, the very next poll hard-targeted the next flagged mob with zero breathing room, which could snowball tightly packed camps. Fresh objective pulls now wait for a **3-second calm window** after the last combat contact: kill → clear all aggro → brief pause → next pull. Mobs already aggroed on you are never delayed — leftovers and wanderers still get finished immediately

## v0.1.35 — 2026-07-20

### Fix — Positional anchor: hazard fallback (mirror anchor instead of parking at the edge)
- When an arena hazard covered the anchor point, the toon walked as close as it could and then just parked — the safety veto refused the spot but nothing tried an alternative. The required arc has more than one valid camping spot: the anchor now falls back to the **mirror-side boundary anchor**, then the **arc center**, and only holds position when every same-arc spot is hazarded (the Nav Control movement line then says so). A toon settled on the mirror anchor counts as "home" — it won't keep re-drifting toward the hazard side

### Fix — Samurai: no more cancelled Midare/Ogi casts while the anchor moves
- Second field report from the live anchor run: SAM started its 1.3s Iaijutsu/Ogi Namikiri cast bars mid-step and the movement cancelled them. Two-sided fix: **movement now hard-stops the instant any cast bar starts** (the run-to-completion hold never carries a path into a cast), and **the SAM rotation holds its cast-time GCDs while moving** — including during Daedalus's own anchor hops — casting the moment the toon plants, with combo/filler GCDs keeping the GCD rolling meanwhile (same rule RSR uses)

### Fix — Positional anchor: no more stutter-stepping
- First live anchor run stutter-stepped, for three compounding reasons, all fixed: **(1)** a drift-back move was stopped mid-hop every time the GCD timer ran down and resumed after the GCD fired (move-stop-move every cycle) — an in-flight anchor move now always runs to completion (mechanic dodges still abort it instantly); **(2)** a target turning ~6° was enough to cancel-and-reissue the running path, which the movement rate-limiter could then suppress, halting the toon mid-hop — the hold tolerance is widened past the rate-limiter's threshold; **(3)** every twitch of a turning mob drags the anchor point along the ring, and the toon chased it step-by-step — drift-back now has a one-GCD cooldown (real flank/rear hops are not delayed by it)

### Fix — Auto movement now works in Trust parties (the anchor's invisible blocker)
- Field-tested the SAM anchor in a Trust party and nothing moved: the auto-movement party gate checked the party list, but **the party list is empty in Trust/Duty Support content** — trust allies only exist in the object table, so every Trust run counted as "solo" and all auto movement (positional anchor, burst approach) was silently blocked. The gate now also scans for trust NPC allies, the same way party-wide healing already does. Genuinely solo play still never auto-moves (by design)

### New — Positional anchor diagnostics in Nav Control (why isn't it moving?)
- Field report: boundary camping switched on, nothing moved, and nothing said why. The Nav Control window (under the Boundary Camping toggle) now shows the **live anchor gate chain**: green "Anchor LIVE" or red "Anchor BLOCKED: {first failing gate}" — the gates being the camping switch, the job's rollout status, the job's positional toggle, the auto-movement master toggle, **the party requirement (solo never auto-moves, by design — this was the invisible blocker)**, the single-target check, and target presence. Below it: the next anticipated positional and the movement service's live verdict ("Moving", "Skipped — at anchor", "would clip GCD"...)

### New — Positional anchor rollout: Samurai joins Ninja
- SAM is the second job cleared for the boundary-camping positional anchor (its Gekko/Kasha anticipation is verified to match the dispatcher exactly, including Meikyo Sen routing). Still behind the Nav Control **boundary camping** switch (off by default) — turn it on for the SAM toon to field-test: expect the toon to park at the flank/rear border ±10°, hop ~1.7y across for Gekko/Kasha, and drift back to the border after knockbacks

### Fix — Viper: coil anticipation + finisher positionals restored
- Third live-sheet correction of the day: the **Flanksting/Hindsting finisher family has positionals on live** (Flanksting Strike + Flanksbane Fang = flank, Hindsting Strike + Hindsbane Fang = rear, 340→400) — the "removed in 7.05" note was stale reference data. The next-positional anticipation also never covered the twinblade chain; it now anticipates **Hunter's Coil (flank) → Swiftskin's Coil (rear)** during Vicewinder chains, outranking the finisher step exactly as the rotation casts them. The whole six-job positional bank (NIN/SAM/RPR/VPR/MNK/DRG) is now verified arc-by-arc against the live game sheets

### New — Positional anchor: drift-back to the boundary (groundwork, off by default)
- First piece of the new positional system (Daedalus owns the angle, BossMod owns dodging): when boundary camping is active, a melee toon inside the correct arc but knocked off its anchor point (the flank/rear border ± the bias angle) now **drifts back to the border during filler GCDs** — movement-budget-clamped, never clipping a GCD, always behind the BossMod safety veto. This keeps every flank↔rear swap a ~1.7-yalm hop instead of an arc-center round trip. Inert until boundary camping is enabled per job (validation rollout comes next)

### Fix — Dragoon: current-patch positionals restored (combo steps)
- Same stale-data correction as Monk, caught by another live tooltip: **Chaotic Spring is rear** (140→180, combo 300→340) — and the live sheet confirms **Chaos Thrust rear, Fang and Claw flank, Wheeling Thrust rear** too (Drakesbane/Heavens'/Spiral Blow have none). The next-positional anticipation was still keying on proc statuses that died in 7.0 and missed the Spiral Blow upgrade id; it's now combo-position based with both base and upgrade ids per step. Combo steps are still never held — this feeds the overlay and the upcoming positional anchor. Hunter's Coil (flank) and Swiftskin's Coil (rear) were sheet-confirmed as already correct

### Fix — Monk: current-patch positionals restored (coeurl form only)
- A live tooltip disproved the earlier "Dawntrail removed all Monk positionals" conclusion (based on outdated reference data) — a later patch re-added them to the **coeurl form**: Demolish is **rear**, Snap Punch and Pouncing Coeurl are **flank** (310→370, Fury 460→520); opo-opo and raptor GCDs have none. The positional overlay/anticipation now reflects this: no more phantom flank/rear prompts on opo-opo and raptor GCDs, the raptor→coeurl step anticipates the correct arc (Fury → flank, Demolish due → rear), and Pouncing Coeurl's arc label is corrected from rear to flank. GCDs remain ungated — this fixes what the overlay and upcoming positional anchor aim at, not when things cast

## v0.1.34 — 2026-07-20

### Fix — Rotation audit batch 2 (fleet jobs): SAM buff gates
- **Samurai**: Ogi Namikiri and Higanbana now require BOTH Fugetsu and Fuka before firing (RSR parity) — previously a post-downtime Ogi could cast without Fugetsu's +13%, and a badly timed Higanbana snapshot locked in a full minute of unbuffed DoT ticks
- NIN, SMN, and AST's damage side audited clean against RSR in the same pass (Ninki pooling, demi-phase chains, and Oracle/Lord burst handling already match)

### Fix — Rotation audit vs The Balance/RSR, batch 1: WAR and BRD damage fixes
- **Warrior**: the Surging Tempest refresh guard now also covers Inner Chaos/Chaotic Cyclone and the Primal Rend/Ruination chain (burning a 660-700 potency cast while the +10% buff lapses lost twice — the unbuffed hit AND the delayed refresh); and Inner Release no longer waits for 50 Beast Gauge (its stacks are free Fell Cleaves — the hold just drifted the 60s cooldown, costing IR windows over a fight)
- **Bard**: Iron Jaws is now the top GCD priority (was below every proc and Apex Arrow) — during Raging Strikes/Battle Voice the continuous proc parade could starve a due refresh past expiry, dropping both DoTs mid-burst
- PLD and SGE audited clean against RSR in the same pass (FoF-keyed burst logic and Phlegma/Toxikon/DoT ordering already match)

### Fix — Pictomancer: burst windows no longer wasted on hardcast Rainbow Drips (major damage fix)
- **The big one**: inside every Starry Muse window, the rotation hardcast ~4-second Rainbow Drips at a priority above the hammer combo, Comet, and the subtractive spells — and re-cast it on every recast, so the entire 20s buffed window drained into slow unbuffed-tier casts. In combat, Rainbow Drip now fires **only as the instant Rainbow Bright proc** (hardcasting is a pre-pull thing), matching The Balance and RSR
- **Comet in Black is now the first GCD priority under Starry Muse** (above Star Prism and the hammer combo), instead of waiting behind the hammers
- **Holy in White no longer spams over stronger spells**: when standing still it fires only as paint-cap protection (5 paint, outside Starry) instead of outranking the subtractive combo as generic filler; while moving it remains the instant of choice at any paint count. The old palette-gauge gate on Holy is gone (Holy consumes paint, not palette — wrong resource)

### Fix — Burst readiness pips actually light up now (and bursts can auto-fire)
- The readiness pips in the Party Coordination window were **always red** because only Blue Mage's Moon Flute sync ever announced readiness — no other job reported, so pips stayed red and the "fire when everyone is ready" auto-burst could never trigger. Every toon now announces readiness on the heartbeat: jobs with a party raid buff (PCT Starry Muse, AST Divination, SCH Chain Stratagem, DRG/BRD/SMN/RDM/DNC/RPR/MNK/SAM/NIN/VPR/MCH) report ready **in combat when that buff is off cooldown**; jobs without one (tanks, WHM/SGE, BLM...) report ready whenever they're in combat — they have nothing to align
- With readiness flowing, the **auto-fire works**: once every toon in the party reports ready, the group's burst window opens by itself (the alert feed logs it like a forced burst). Guard rails: auto-fire needs at least one raid-buff job in the group (an all-tank/healer group would just cycle the window forever — Force burst still works there) and won't re-fire within 30 seconds of the last window
- BLU is unchanged — its readiness still comes from the Moon Flute coordination path

## v0.1.33 — 2026-07-19

### New — Plugin checklist in Settings → General
- A collapsible **Plugins** section now shows the companion plugins Daedalus works with, with live install/enable state and version: **Required** — vnavmesh (movement/navigation) and BossMod Reborn (mechanic safety; VBM also detected); **Optional integrations** — AutoDuty, Questionable, Henchman, Charon, and Caduceus. Green check = enabled (with version), yellow = installed but not enabled, red X = not installed; hover any row for what the plugin is used for. A summary line confirms "All required plugins are installed" or warns when one is missing

## v0.1.32 — 2026-07-19

### Fix — All healers: group heals are forced when the whole party is critical (1-HP mechanics)
- After abilities that drop everyone to 1 HP, recovery was serialized: the cross-healer AoE dedup let one healer group-heal while continuously vetoing the other's every AoE heal (it re-reserved each frame), and single-target triage outranked AoE in the weave order — so the party crept back up one heal at a time. Now, when several party members are critically low at once (2+ below the GCD Emergency threshold, measured on raw HP), an **AoE emergency** kicks in for **all four healers**: group heals bypass their min-target/average-HP gates, co-healer deferral, and the cross-healer reservation — both healers dump AoE heals simultaneously — and AoE recovery outranks single-target triage for weave slots
- Per healer: **SGE** Ixochole/Physis II/Holos/Pneuma/Prognosis; **WHM** the Medica/Cure III line (no more waiting on Thin Air in an emergency); **AST** the Helios line + Celestial Opposition; **SCH** Succor and Indomitability — Scholar will also spend Aetherflow through its configured reserve for the emergency Indomitability (a stack is still required)
- Debug panels show "AoE EMERGENCY" on the firing handlers. New shared toggle: Sage settings → "Force Group Heals in Emergency" (on by default, applies to every healer); normal chip-damage behavior is unchanged

## v0.1.31 — 2026-07-19

### New — Burst signals are visible in the alert feed (field-test check)
- The Party Coordination alert feed now logs every burst signal so you can verify scoping in-game: gold **"burst window open — {toon} (party A)"** when a window opens (shows who pressed it and which party it reached; "(fleet-wide)" for ungrouped), and grey **"burst ignored (other party) — {toon}"** when the group gate drops another party's signal — the proof the scoping works. The BURST WINDOW OPEN banner also shows its remaining seconds

### Fix — Roster no longer shows a permanent "BURST" on tanks and healers
- WAR/SGE stayed gold "BURST" forever while the PCT correctly aged to "burst 120s ago": a party member's raid buff (e.g. Starry Muse) opened the burst window on every toon via the cast event, but only DPS jobs (and AST) run the code that ever closes it. Burst windows now expire on their own when the buff duration runs out, so the last-burst column ages honestly on every job — and tank/healer burst-timing reads (cooldown pooling gates) no longer see a stuck "in burst" state either

## v0.1.30 — 2026-07-19

### Change — LAN coordination: bursts and tank swaps are now scoped to the issuing party
- **Force burst (and the automatic all-ready burst) now only touches the party that issued it** — previously the signal hit every Daedalus toon on the LAN, so two groups running at once would trip each other's burst windows. Signals are stamped with the sender's in-game party; toons in a different party ignore them. The Force burst button's tooltip shows which reach applies
- **Automatic burst readiness is per-party too**: a group fires when *its own* members are ready instead of waiting on every toon on the LAN — two parties burst independently
- **Manual tank swap is party-scoped the same way**
- Ungrouped toons (solo, or mid-zone-in) keep the old everyone behavior in both directions, so nothing breaks while parties are forming
- **Burst readiness strip fixed and now per-party**: the "=" boxes were a lightning-bolt character the game font can't draw — pips now use real icons (gold bolt = ready, red circle = not ready, hover a pip for the toon's name). With two or more parties on the LAN the strip shows one readiness row per party (letter-matched to the roster's group dots), so each group's own count is visible at a glance

## v0.1.29 — 2026-07-19

### Fix — Party members loading late are no longer invisible (Kardia on the wrong ally)
- **A toon that zoned in slower than the rest could stay invisible to a healer for the whole instance** — the party scanner cached member ids by party size, and with staggered multibox zone-ins a still-loading member's placeholder id got latched forever. Field symptom: one of two Sages saw no tank at all (tank id 0), put Kardion on the Pictomancer, and never moved it home. The cache now revalidates against the live party list every frame, so the tank is picked up the moment their client finishes loading — this fixes tank resolution, heal targeting, and every party scan on all healers
- **Kardia no longer falls back to a DPS while the tank is still loading**: pre-pull, a tank listed in the party that isn't loaded on this client yet shows "Waiting (tank loading)" instead of parking Kardion on a DPS. In combat the fallback stays (any bearer beats none), and genuinely tankless parties behave as before

## v0.1.28 — 2026-07-19

### New — Resurrection audit for raids: Summoner raises, BLU raises, raid triage order
- **Summoner never raised at all** — the classic battle-rez third had no resurrection handling. It now Swiftcast→Resurrections like the healers (hardcast per the same global Resurrection settings), with the cross-box raise reservation so two toons never rez the same corpse
- **Blue Mage now actually casts Angel Whisper** (healer role): the fleet-sting design always reserved a healer-mimic "for cleanup raises", but nothing ever cast it. Swiftcast when up, 10-second hardcast per the hardcast setting, own 5-minute cooldown respected
- **Everyone raises in raid triage order** — healers first (they raise everyone else), then tanks, then DPS — instead of raw party-list order, which could raise a DPS while the other healer stayed on the floor. Applies to WHM/SCH/AST/SGE/RDM/SMN/BLU alike; corpses already carrying a pending Raise (from anyone, Daedalus or not) are always skipped

### Fix — Hardcast raises now STOP the toon to cast (the real alliance-raid culprit)
- With hardcast raise enabled and Swiftcast down, the raise still never happened — because BMR AI micro-moves the toon near-constantly (follow/dodge), the "not moving" gate never opened, and the 8-second cast could never start. A hardcast raise now requests a **movement hold**: BMR AI steering pauses, the toon stops, the cast goes out, and movement resumes automatically (the hold is expiry-driven, so it can never stick). Applies to every raiser's hardcast path — WHM/SCH/AST/SGE/SMN and BLU's 10-second Angel Whisper

### Fix — Raises actually fire on your own LAN boxes: Swiftcast theft + reservation squatting
- The real reasons a dead multibox toon stayed on the floor: the Sage's **emergency-heal Swiftcast** didn't know a raise was pending — under raid healing pressure it burned Swiftcast on saves the instant it came up, and with hardcast raise off the raise waited for a Swiftcast that never survived to its turn. A pending raise now owns Swiftcast (a corpse outranks a low-HP save)
- **Reservation squatting between boxes fixed**: raisers reserved the corpse the moment a raise became a *candidate*, before the cast ever dispatched — so a box whose raise kept failing to go out re-reserved every frame and locked every other box out of the corpse indefinitely. Reservations now go out at actual dispatch, when the cast is real

### New — Alliance-raid raises + last-burst on the roster
- **Raises now reach the whole alliance**: the finder only scanned your own 8-man party, so a corpse in another alliance party was invisible even though raise spells can target any alliance member — the reason a Sage stood over a dead alliance toon doing nothing. When nobody in YOUR party needs a raise, the scan now falls back to dead alliance players (triage-ordered, instanced duties only, toggleable under Settings → General → Resurrection). Reminder: "Allow hardcast Raise" is OFF by default — with Swiftcast down, a raise waits for it unless you enable hardcasting
- **The Party Coordination roster now shows each toon's last burst** where "synced" used to sit: gold **BURST** while the window runs, "burst 34s ago" after, plain "synced" until a toon's first window — drift between boxes is visible at a glance

### Fix — Sage: Kardia works with two Sages in the party
- With a second Sage in the group, your Sage saw the co-Sage's Kardion on the tank, decided its own was already placed, and never put its Kardia up at all — the tank should carry BOTH (each Sage's Kardion heals independently). With a co-Sage present every Kardion check is now source-aware (only YOUR buff counts) and the trust-NPC inference shortcut is disabled (it can't tell whose invisible buff it's guessing about). Solo and single-Sage behavior is completely unchanged

## v0.1.27 — 2026-07-18

### Fix — All melee DPS: automation engagement + positional audit (the Monk lesson, generalized)
- **Every melee job now opens on automation hunt marks**: SAM, NIN, DRG, RPR, and VPR had the same dead-end the Pugilist did — beyond melee range a passive hard-targeted mark produced "No target" and nothing was ever queued, leaving the driver waiting forever. All five now keep their combo starter queued on the mark and fire the instant they're walked into reach. Manual play is untouched
- **Dead positional rules removed**: Dragoon's enforce-holds (all DRG positionals were removed in 7.0) and Viper's finisher hold (the Flanksting/Hindsting family lost its positionals in 7.05) could stall combos waiting for a position that no longer matters — both gone, verified against RSR's action data
- **Reaper keeps its real positionals (Gibbet flank / Gallows rear) but never deadlocks solo**: enforcement now only holds in a party, where positional movement can actually act — solo play (where the mover is disabled by design) casts through instead of burning time-limited Soul Reaver stacks waiting for nobody
- SAM and NIN came through the audit clean: real positionals, no holds, correct behavior

## v0.1.26 — 2026-07-18

### Fix — Monk/Pugilist: engages with automation drivers, positional hold removed
- A Pugilist on the Henchman bridge stood at its hunt mark doing nothing: beyond melee the module had no candidate for a PASSIVE mark (the engaged-enemy scan can't see it, and sub-15 there's no ranged tool or Meditation), and in melee an old positional rule could hold the opener with "Moving to rear" forever. The opener now stays queued on the automation hard target (it fires the instant you're walked into reach) — and the positional hold is gone entirely: **Dawntrail removed every Monk positional**, so nothing is lost and True North/positional settings no longer gate Monk GCDs

## v0.1.25 — 2026-07-18

### Fix — Black Mage: low-level ice phase now exits at full MP
- Round three from live testing: fire→ice via Transpose worked, but the ice phase then chain-cast Blizzard forever at full MP. Cause: the phase insisted on building to Umbral Ice III before anything else — and at low level Blizzard I only ever REFRESHES Umbral Ice I, so the exit could never be reached. The full-MP exit now comes first: MP restored = Transpose back to fire (sub-35) or Fire III as before at 35+. The complete low-level loop is confirmed working through the fire side; this closes the ice side

## v0.1.24 — 2026-07-18

### Fix — Black Mage: low-level fire↔ice ping-pong
- Round two from live testing: at low MP the toon alternated Fire-Ice-Fire-Ice forever. The game rule at fault: a plain Blizzard cast in Astral Fire only STRIPS the fire stacks — it does not grant Umbral Ice (only Blizzard III hard-swaps) — so the "transition" landed on neutral and the rotation opened with Fire again on a drained tank. Two fixes: the sub-35 fire→ice swap is now an instant Transpose (Blizzard-strip only as fallback while Transpose's recast rolls), and from neutral with under 7,200 MP the rotation now opens with ICE, never Fire. The loop is now: Fire ×N → Transpose → Blizzard refill ×N → Transpose → Fire ×N

## v0.1.23 — 2026-07-18

### New — Black Mage: Transpose in the low-level loop
- Leaving Umbral Ice by hardcasting Fire only REMOVES the ice stacks without granting Astral Fire — a dead 2.5-second cast and wasted MP, then a second Fire to actually start the phase. Below Fire III the ice→fire swap is now an **instant Transpose weave** that rides the refill Blizzard's cast tail (zero added latency), with the hardcast kept as fallback only while Transpose's 5-second recast is rolling. Fire III transitions at 35+ are untouched. Completes the sub-60 loop from v0.1.22: Fire ×N → Blizzard (refill) → Transpose → Fire ×N

## v0.1.22 — 2026-07-17

### New — Blue Mage: fleet Final Sting (v3.4 — the Coil finisher)
- **The Party Coordination window gains a FLEET STING button** (Ctrl-gated — it kills the stingers): plans how many toons need to sting from the boss's live HP and your Final Sting Calculator calibration (over-provisioned by a configurable safety factor; an uncalibrated fleet sends everyone), broadcasts the order, and shows you exactly who stings in what sequence before you press it
- **Staggered, not simultaneous**: stingers execute 3 seconds apart in a deterministic order, each one re-checks the boss first, and the moment the boss dies every later slot aborts and resumes its rotation — no wasted corpses. If a queued stinger dies, the order shifts up automatically
- **Safety rules**: participation is a per-toon opt-in ("Join fleet Final Sting orders", default OFF), the tank-role toon never stings, and one healer-mimic is always held back so Angel Whisper can raise the fallen. **Auto-trigger** in Coil T5 (25%) and T9 (15%) when opted in; T13 is manual-only (phase-dependent)
- **Fleet mimicry buttons** in the same window: Mimic Tank / DPS / Healer / Remove — one press, every BLU box applies it to itself

### Fix — Black Mage: sub-60 leveling actually casts Blizzard
- A low-level Black Mage (anything under Fire IV at 60 — including the whole sub-30 Thaumaturge band) spammed Fire until its MP ran dry and then just stood there: the low-level branch never checked MP and the ice transition was unreachable, so Blizzard never cast. Now it fires while MP lasts (RSR-matched floor: doubled Astral Fire cost + buffer) and Blizzards into Umbral Ice to refill, exactly like the high-level loop

### Fix — Blue Mage: manual mimicry actually casts with Auto Mimicry off
- A manual/fleet mimicry request with the Auto Mimicry toggle OFF found its target (another BLU works fine as a DPS source) but never cast — the dispatcher was re-checking the AUTO toggle and silently rejecting the cast, then the 4-second retry window blacklisted the innocent target. Manual requests now ride a toggle-free path, as the window always promised

### New — Blue Mage: co-op freeze→shatter (v3.6)
- With 2+ BLU on the bus, exactly ONE toon freezes (simultaneous Ram's Voices are wasted GCDs and Deep Freeze re-application builds resistance) and exactly ONE shatters — everyone else holds damage while a fresh freeze is on the pack, so nobody breaks it early. No shatter-capable toon in the fleet = nobody freezes at all
- **Pick the shatter toon yourself**: the Party Coordination window's BLU fleet section gains a "Shatter" dropdown — your pick outranks the automatic (alphabetical) election on every box within ~2 seconds, and "Auto" hands it back. Toons without Ram's Voice/Ultravibration slotted can't be picked
- The hold is time-bounded: if the shatter hasn't come within 5 seconds (owner's Ultravibration on cooldown, owner died), the fleet resumes damage instead of idling out the full 12-second freeze. A dead freeze-lead or shatter-owner re-elects within a second

### New — Countdown-synced pre-pull tinctures (LAN Phase 2)
- **Start an in-game countdown and every toon pots itself before the pull**: casters and healers at T-2s, everyone else at T-1.2s — one pot per countdown, cancelled countdowns never pot. Works fleet-wide: partied toons read the countdown directly; unpartied fleet members get the shared T0 over the LAN (with a clock-sanity guard)
- **Tinctures now work outside high-end content (opt-in)**: the auto-pot system silently required a savage/extreme/ultimate zone — in Coil clears, dungeons, and farm runs it never fired and never said why. A new Consumables toggle ("Allow outside high-end content") opens it up; the burst-window and pull-intent triggers are unchanged. This was the answer to "I have use-tincture on but never saw it pot"

### Polish — UI theme sweep
- The remaining windows moved onto the Daedalus gold/dark identity: Nav Control, Control, Missing, Training, Fight Summary, and the Action Feed now use the shared status palette (no more per-window greens and reds), section headers got the gold treatment, and fight grades run gold-S through red-D

## v0.1.21 — 2026-07-16

### New — Blue Mage: manually-cast Missiles now teach the Death Ledger
- The ledger only learned from casts Daedalus dispatched itself — hand-played Missiles taught it nothing. It now also observes YOUR manual Missile casts through the action-effect hook, which fires even on a full resist ("Gilgamesh takes no damage" = zero damage events, so watching damage alone would have missed exactly the Immune verdict). A cast the rotation dispatched is never double-counted
- **A missed Missile can't create a false Immune**: the packet's effect type is read directly — damage landed = Weak instantly, full resist/invulnerable = Immune, and a whiffed accuracy roll = no verdict at all (previously the untouched HP bar after a miss could brand a vulnerable species Immune, and the rotation would never probe it again). Field-verified against real log lines: a roll failure prints "The attack misses", true immunity prints "Full resist!" — the ledger tells them apart on the wire
- Once a species lands as Weak it stays **Weak forever** — later resists and misses can't flip it. The ledger display is now just the verdict (Weak/Immune), no confirm counter

### Fix — Nav Control: BMR AI on/off is now tracked
- The Nav Control panel showed the preset and config-push status but had no idea whether BMR AI mode itself was running — you could stare at all-green lines with /bmrai off (or wonder why it says nothing when it's on). BossMod exposes no query for this, so Daedalus now reads BMR's own "AI: On/Off" server-info-bar entry: the panel shows **BMR AI mode: ON** in green, a red "nothing moves until /bmrai on" when it's off, and an honest "unknown — enable Show DTR in BMR's AI settings" when the status-bar entry is hidden

## v0.1.20 — 2026-07-16

### New — Blue Mage: Missile cheese + the Death-Immunity Ledger
- **Missile chain**: in duties, big targets that aren't death-immune get Missile spam (50% of CURRENT HP per cast) down to a configurable HP floor, then the normal rotation finishes. Four Missiles = 94% of a boss gone
- **The ledger nobody published**: no public list exists of which bosses the death-family spells (Missile, Tail Screw, Launcher, Level 5 Death, Ultravibration — one shared immunity flag) actually work on. Daedalus now builds yours automatically: every Missile cast is a recorded probe — the target's HP tells the truth — and the verdict persists forever. Unknown bosses cost exactly one probe cast; known-immune bosses are never wasted on again. Clear old dungeons on BLU and watch the list grow in the BLU window (✓ vulnerable / ✗ immune, per zone)
- The intended loop: Missile-chain the vulnerable bosses, **Final Sting** the immune ones (the Solo execute pairs with this), with an invuln-phase safeguard — one later successful hit permanently corrects a false "immune"
- **The ledger lives in the Raid window** (it's per-duty aware): inside a duty you see that duty's verdicts inline — "Blue Mage — Death: Weak/Immune" per boss — and the full learned list sits below, grouped by zone, with the clear button

### New — Blue Mage: multi-BLU fleet coordination (v3)
- **Every BLU toon now advertises its capabilities on the LAN** — which coordination-relevant spells are slotted (DoTs, Moon Flute, Gobskin, Cactguard, Level 5 Death, Sticky Tongue, Avail…) ride the roster heartbeat, and every box deterministically elects the same owners with zero negotiation. A toon whose loadout lacks a spell can never be assigned it; solo or single-BLU play is completely unchanged
- **One owner per DoT**: with 2+ BLU on the bus, exactly one toon owns the bleed (Song of Torment — the status is shared, so a second caster was silently clobbering snapshots), one owns Mortal Flame (any recast REPLACES the permanent snapshot), one owns Breath of Magic. Non-owners skip those casts entirely; the bleed owner refreshes with a Bristle snapshot only when the DoT would otherwise drop, never overwriting a buffed snapshot with a plain one
- **Synced Moon Flute windows**: each toon announces when its burst pieces are ready, and the shared burst signal starts every window on the same tick. In T13 the fleet auto-splits into two groups 30 seconds apart, so half the party is never locked in Waning when a Gigaflare push needs Mighty Guard back up. Toggleable; solo Flute timing untouched
- **One Gobskin per fleet**: the barrier doesn't stack — the healer-mimic (250 vs 100 potency) wins the election, everyone else suppresses, and even the owner yields when a real SCH/SGE shield is already on the party
- **Cactguard the tank**: when BossMod forecasts a tankbuster within 5 seconds, one designated non-tank casts Cactguard on the tank (5% damage down — 15% if the caster runs tank mimicry). Works with a real tank or the tank-mimic BLU
- **Coil pre-pull checklist in the Party Coordination window**: inside T5/T9/T13 the window shows each utility slot's assigned carrier — Level 5 Death (T5 ×1, T13 ×2), Sticky Tongue (T9 ×2), Avail (T13 ×2) — green when covered, **red when the fleet can't cover it** so you fix loadouts before the pull, plus the T13 Flute stagger groups. The mechanics themselves stay manual by design
- Debug window (Blue Mage tab) shows the live owner election ("2×BLU bleed:me MF:Saar BoM:me …")

## v0.1.19 — 2026-07-12

### New — Blue Mage: SOLO role
- The Role dropdown gains **Solo** — the overworld/farm mode: **Basic Instinct first, then Mighty Guard on top** (Basic Instinct cancels the stance's damage penalty, so you tank AND hit at +100%), DPS mimicry, White Wind and Diamondback self-sustain, Goblin Punch as the 400-potency melee filler under the stance
- Picking Solo casts Basic Instinct + Mighty Guard **immediately, out of combat, on zone-in to a duty** — no first-pull GCDs wasted on setup. Basic Instinct is duty-only (the game refuses it in the open world), so overworld Solo runs at full damage without the stance; unsynced dungeon farming is where the BI+MG combo shines. The Solo option is **locked while you're in a party**, with a clear warning instead of a silently broken mode
- New **Solo reference loadout** for the Apply/auto-load buttons: the DPS kit with Basic Instinct, Mighty Guard (in place of Revenge Blast), White Wind (in place of Off-guard), Final Sting, and the freeze→shatter pair
- **Final Sting execute** (off by default): on the last engaged enemy at/below a configurable HP%, Solo role only — it kills your character and locks itself out for 10 minutes, so it's for finishing tough solo targets, never farm loops

### New — Blue Mage: Mimicry window (manual role control + REAL removal)
- A **BLU Mimicry window** pops up when you switch to Blue Mage (toggleable): **Mimic Tank / Mimic DPS / Mimic Healer** buttons that scan the area for a player of that role and cast on them — a fresh cast overwrites the current role buff, so switching roles never needs a removal. Works even with Auto Mimicry turned off, and (unlike auto) works inside duties with real players
- **Remove mimicry actually works now**: casting Aetheric Mimicry with NO target strips the buff (it can't target self) — the window's Remove button uses that trick, and the loadout Apply flow now removes the buff BY ITSELF instead of asking you to job-swap: press Apply, mimicry drops, the set swaps, auto-mimicry re-buffs you
- Auto-mimicry pauses 15s after a manual removal so it doesn't instantly re-buff you

### New — Blue Mage: Final Sting damage calculator
- The BLU window gains a **Final Sting Calculator**: calibrate it with one observed non-crit hit (an unbuffed Sonic Boom, or a real Final Sting test), then toggle buffs — Moon Flute ×1.5, Whistle ×1.8 (the sting is physical, so Bristle does NOT apply), Off-guard, Basic Instinct, Mighty Guard — and see the estimated sting damage as a guaranteed non-crit floor
- Enter a target's HP and a safety factor and it tells you **how many simultaneous stings the kill needs** — the planning math for Coil-style multi-BLU sting finishes. The calibration persists for the future fleet-sting automation

### Fix — All jobs: a toon left facing the wrong way now turns itself back
- When something external turns the character (BossMod dodging an attack, a movement tool), every targeted cast gets refused with "not facing target" and — with client auto-face unable to help — nothing ever rotated the toon back: one BLU run sat 27 seconds casting nothing. Facing rejections now trigger an immediate recovery that physically turns the character toward the target (throttled, never mid-cast), on top of the existing hard-target nudge
- Interrupted buffs already self-heal (the rotation re-casts anything whose status didn't appear — that part was working)

### Fix — All jobs: spamming the same spell no longer loses the queue window
- Re-queuing the SAME spell for the next GCD (Sonic Boom→Sonic Boom, Glare→Glare — every single-filler rotation) was rejected by an overzealous duplicate guard, wasting the early-queue window every time. On BLU it was visible as Sonic Boom and Water Cannon alternating every other GCD; on other jobs it just shaved reaction time off every repeat cast. Same-spell re-queue is now allowed inside the queue window only — the double-submit protection everywhere else is unchanged

### Fix — Blue Mage: third/fourth-field-run corrections
- **Self-buffs no longer double-cast** (Toad Oil went up twice back-to-back): a 2-second cast's buff appears a beat after the cast ends, and the very next decision still read "missing" — each self-buff now waits 5s after a cast before retrying (a genuinely interrupted cast still retries after that)
- **A movement-interrupted Mortal Flame no longer locks the spell out for a minute** — its safety latch (built when the DoT's status id was unverified) shrank from 60s to a 5s latency guard now that detection is field-proven
- **Recast-cooldown spells can't double-fire in the queue window** (Matra Magic cast twice back-to-back, burning the 120s cooldown for one hit of value): the same-spell requeue exemption now applies to plain fillers only
- **Breath of Magic actively turns you toward the target**: after a dodge left the toon mis-faced, the self-cast cone just waited (nothing rejects a self-cast, so nothing triggered the turn-around) — 8 dead seconds. Wanting to cast it while mis-faced now physically rotates the character at the target first
- **Surpanakha never presses at GCD-idle anymore** — a press with no GCD rolling just delayed the next cast (a clip for 200 potency); it now weaves only inside real weave slots
- **Action log names the right cast**: a queued next-spell submit could overwrite the previous cast's pending log entry before it flushed, labeling it with the wrong name (a Toad Oil showed as "Bristle", then as "Mighty Guard"). Committed casts now flush both on the in-window path AND at GCD-idle, and dropped queue submits are discarded instead of logging phantom casts — all jobs

### Fix — Blue Mage: second-field-run corrections
- **Bristle's boost now always lands on the DoT it was cast for**: while the boost is armed, every other damage cast — including oGCD weaves, which are spells too — holds until the snapshot fires (a run showed three Bristles and zero Breath of Magic, the boosts dying on Glass Dance and Rose)
- **Surpanakha can actually chain now**: charge-based abilities are exempt from the 1-second same-action repeat guard, so back-to-back presses go through (the game's own charge count is the real limit)
- **Toad Oil** no longer pre-buffs in the open world — it casts in combat/duties only

### Fix — Blue Mage: first-field-run corrections
- **Breath of Magic and Bad Breath now check that you're actually FACING the target** — they're self-anchored cones the game never auto-turns for, and a wrongly-faced toon spammed Breath of Magic twelve times into empty air. A per-target latch also caps it at one cast per 10s no matter what
- **Cold Fog** no longer burns its 90s cooldown on a pack that's about to die
- The Debug tab now says **why Basic Instinct/Toad Oil are holding** ("held — party present") instead of silently skipping

## v0.1.18 — 2026-07-13

### Changed — Party invites moved to Charon
- The invite buttons are gone from the LAN party window — party invites now live in the companion [Charon](https://github.com/ofnature/Charon) plugin (Group Management), which can invite the whole fleet in one click. The LAN window is now a pure status/coordination display; all its other tools (HP bars, roles, burst, tank swap, target modes, scramble) are unchanged. The roster Daedalus shares with Charon now carries the addressing its native invites need.

## v0.1.17 — 2026-07-11

### New — Farm mode: mounted travel
- Farm toons now mount up for long legs (spot-to-spot and back-to-spot travel): Mount Roulette by default or a specific mount of your choice, flying where the zone allows it, with clean dismount-before-engage. New Travel section in the Farm window (mount mode, distance thresholds, acquisition scan radius, fly toggle)

### Improved — Auto tank swap: real buster stacks only
- The stack-count auto-swap trigger now recognizes actual "damage taken increased" stacking debuffs (the Vulnerability Up family, built from game data) instead of counting any stacked status on the co-tank — no more swaps armed by unrelated stacking effects. Fight-specific exceptions can be registered per territory

### New — Blue Mage: full support for the standard solo/farm loadout
- The rotation now actually uses the whole 24-slot kit: **Bristle-snapshotted DoTs** (Breath of Magic upkeep, Mortal Flame cast exactly once per target), **Matra Magic** on cooldown, **Cold Fog → White Death** (armed when something is about to hit you; getting hit unlocks 15s of instant 400-potency spam), **Bad Breath** once per pack, and off-cooldown weaves of **Feather Rain, Glass Dance and Both Ends**
- **Surpanakha** dumps all 4 charges strictly back-to-back — the rotation refuses to weave anything else mid-dump, since any other action drops the stacking buff
- **Freeze → Shatter**: The Ram's Voice freezes the pack, Ultravibration instantly KILLS everything frozen around you. All other damage is held between freeze and shatter (damage breaks the freeze), freeze-immune packs are detected once and skipped for the rest of the fight. Min-target slider in Settings → Blue Mage → AoE
- **Basic Instinct** (+100% damage while solo — the farm multiplier) and **Toad Oil** (+20% evasion) are kept up automatically in solo/tank play
- Healer role rounds out: **Pom Cure** on the most injured ally (only with the healer mimicry — it's a 100-potency joke without), **Gobskin** barrier upkeep, **Exuviation** cleanse
- Moon Flute's Waning Nocturne lockout is now understood everywhere — the rotation idles through it instead of pushing casts into a locked-out player

### New — Blue Mage: Moon Flute burst window (opt-in)
- With **Moon Flute burst window** enabled (Settings → Blue Mage → Damage, OFF by default), the rotation fires Moon Flute when every slotted big cooldown is ready — Matra Magic, Rose, Both Ends, Glass Dance, Feather Rain, Surpanakha at 4 charges — then runs the buffed burst through the normal priority chain
- Inside the window, Breath of Magic and Mortal Flame are deliberately re-snapshotted (a Flute-buffed DoT beats an unbuffed one), utility casts like Cold Fog are suppressed, and the 15s Waning Nocturne lockout afterwards is waited out cleanly
- A time-to-kill gate holds the Flute when the pack is about to die (configurable, the "don't Trick a dying pack" rule at 10× the cost)

### New — Blue Mage: one-click role loadouts
- The Missing window's role checklists grew an **Apply learned spells** button — it replaces the active 24-slot set with that role's Blue Academy reference loadout, skipping spells you haven't learned (uses the game's own spellbook Load machinery; disabled in combat and inside duties)
- The game refuses spell-set changes while **Aetheric Mimicry** is active, and the buff genuinely cannot be cancelled (not by right-click, /statusoff, or any plugin — only a job change drops it). So a blocked apply now waits up to 30 seconds and tells you exactly what to do: swap jobs for a second to drop mimicry, and the set applies the instant you're back — then auto-mimicry re-buffs you
- Optional **Auto-load role loadout** setting (off by default): changing the Role dropdown swaps the spell set automatically once you're out of combat and outside a duty. Leave it off if you run hand-built loadouts — it replaces the whole set

### New — Companion-plugin bridge grows vitals and a cross-machine relay (for Charon)
- The LAN roster IPC now includes each toon's **HP and entity id**, so a companion healer plugin can watch fleet vitals across machines (Charon's Heal Watch)
- New **generic plugin relay**: companion plugins can publish messages to their siblings on every game client — including two clients on the same PC — via `Daedalus.Relay.Publish` / `Daedalus.Relay.Message`, ferried over Daedalus's LAN transport (Dalamud's own IPC can't cross game clients)

### Fix — Settings checkboxes now apply immediately (all jobs)
- Every checkbox in Settings saved BEFORE writing your change: the running rotation kept the old value (and so did the settings file — a plugin reload could even revert the click) until some other setting was changed. Found via Blue Mage's Auto Mimicry ignoring its toggle; the fix applies to every checkbox in every job section

## v0.1.16 — 2026-07-10

### Fix — Healers anchor on the MAIN tank in two-tank parties
- With two tanks in the party, Kardia (and every other tank-anchored healer behavior — Sage tank oGCDs, Kerachole/Haima anchoring, Earthly Star placement, defensive targeting) picked whichever tank sat first in the party list, which could be the off-tank. "The tank" now resolves to the main tank: the one the biggest engaged enemy is targeting (so a trash mob on the off-tank can't outvote the boss), with the Party Coordination window's off-tank designation breaking the tie before the pull
- Bonus: because aggro reality wins, Kardia automatically follows coordinated tank swaps — after a swap it re-homes to the new boss holder within a few seconds

## v0.1.15 — 2026-07-09

### Maintenance — Backend maintenance
- Internal coordination-layer cleanup; no gameplay changes

## v0.1.14 — 2026-07-08

### New — Coordinated tank swaps (all four tanks)
- Two Daedalus tanks now hand the boss off cleanly: the incoming tank pops a personal mitigation, announces the swap, and Provokes only after the current tank confirms; the current tank Shirks the moment the boss turns. No cross-Provoking — the tank that just gave aggro away is barred from snatching it back, and the designated off-tank never grabs the boss outside a coordinated swap
- Trigger it from the **Swap tanks** button in the Party Coordination window (works pressed on any box, reverses direction automatically on repeat presses), or opt into **auto-swap on buster stacks** with a configurable stack count. Everything is opt-in: Settings → Tanks → Shared → Tank Coordination, on both tank boxes
- New per-toon **tank role** setting (Main tank / Off-tank / Auto) — the tank analog of the healer role preference; Auto follows the Party Coordination window's off-tank picker
- The new shared tank settings page also surfaces previously hidden knobs: Auto Provoke + delay, Auto Shirk, and the defensive/invulnerability stagger windows

### New — One-click multibox grouping
- An **invite button** appears next to any roster toon not in your party — it uses the game's native invite (multi-word names and cross-world within your data center both work, unlike the /invite text command)
- **Party-group dots**: a colored dot between name and job shows which toons share an actual in-game party — same color, same party
- Companion-plugin bridge: Daedalus now exposes its LAN roster over IPC (`Daedalus.Party.GetRosterJson` / `GetTrustListJson`) for helpers like Charon

### Fix — Same-machine coordination actually connects
- Coordination messages (tank swaps, heal/raise reservations, defensive staggering) never worked between two game clients on the SAME PC — the LAN layer assumed local delivery happened elsewhere and dropped its own machine's traffic. Multiboxing on one machine now coordinates identically to cross-machine setups. (This is what made the tank-swap button look dead on first try)

### Fix — Blue Mage pre-flight hardening
- Spells missing from the active set are now skipped cleanly everywhere (previously an unslotted Aetheric Mimicry could burn the retry logic through every party member), Diamondback no longer start-cancels its cast while moving at panic HP, and Aetheric Mimicry is never cast inside a duty — the buff is permanent until recast, so grab it in town before queuing; the Missing window says exactly that when you forget
- The Missing window's spellbook checklist now renders real check/dot/cross icons (the old glyphs showed as "=" boxes) and labels partially-ready spells "learned, not slotted"

## v0.1.13 — 2026-07-08

### Fix — Settings: job validation chips match reality, update checker reads fresh and shows your version
- The per-job validation chips in Settings had fallen behind: Scholar, Dragoon, Black Mage, and Summoner all completed their full in-game validation passes but still showed "untested"/"pending" — now marked **validated**. Bard moves to **pending** (real mid-level pass done, higher-level milestones remain) and Sage moves to **pending** until the new Kardia behavior gets its live run
- "Check for Updates" could read a cached copy of the version manifest for up to ~5 minutes after a release, and its "Up to date" label printed the *remote* version — so right after an update it could claim "up to date (v0.1.11.0)" while you were already running v0.1.12. Every check now fetches fresh, and "Up to date" shows the version you're actually running

## v0.1.12 — 2026-07-07

### Improved — Party Coordination window: mock-parity layout, scrambled machine names, sturdier mode sync
- The burst readiness strip is now a proper bordered panel with per-toon lightning pips and a gold **Force burst now** button; the status summary line is mode-aware (e.g. `Focus: 2/3 DPS on Gate Sentry · Rosa down · MT holds boss`), rows show a gold `→ focus` marker for DPS actually on the party focus target, dead toons get a dark DEAD bar with a red role chip, and latency sits flush right
- **Scramble Names** now also covers machine names (hostnames become Greek places like `Olympus`, `Delphi`) and the alert feed — alerts substitute toon aliases at display time, so even alerts that fired *before* you flipped the toggle are covered in screenshots
- Two sync robustness fixes: switching the target mode back to **None** is re-announced for a few seconds so a single lost network packet can't leave other PCs enforcing a dead mode, and the targeting override now applies/clears immediately on job changes or login instead of waiting for a zone change

### Fix — Sage: Kardia no longer chain-casts when the tank leaves, and actually moves to hurt allies
- When the tank left the zone mid-session (left party, disconnected, teleported away), the Sage would re-cast Kardia every couple of seconds forever instead of settling on a fallback target. The internal "Kardia is on the tank" lock was only ever cleared on a zone change — never when the tank itself disappeared — so the rotation kept asserting a placement that no longer existed while churning targets underneath. The lock now drops when its bearer dies (instantly — the buff strips on death) or has been gone for a few seconds, and every new placement locks onto its actual target so one cast settles it
- Kardia's smart swap now really works: with "Kardia swap" enabled, Kardion moves to the most injured party member below your swap threshold during combat, then returns to the tank once they've recovered — respecting the 5s swap cooldown so it never thrashes. This logic existed as a setting but was never wired into the rotation; Kardia was tank-or-nothing in practice
- New fallback order when there is no tank at all: the hardest-hitting DPS ally (ranked by the built-in parser mid-fight — they're holding the aggro), then yourself. Previously it churned on whoever was lowest HP at that moment

## v0.1.11 — 2026-07-07

### New — Party Coordination window: party-wide target modes, alerts, and burst control
- The LAN Party Coordination window is now a control surface, not just a roster. **Target modes** steer the whole party's targeting from one place: **Focus** (everyone burns one enemy you pick — click it in the window or use your in-game target — and ignores adds), **Split** (DPS spread across the pack, balanced by time-to-kill so everything dies together), and **Kill Adds** (DPS and a designated off-tank peel to adds). The Main Tank is never pulled off the boss — that's a hard guarantee; nominate an off-tank explicitly if you want one moving to adds
- New at-a-glance signals in the window: an **alert feed** (all-healers-down, tank swap, add spawn, raise incoming), a **DEAD** flag with a raise marker, per-toon **burst-ready pips** plus a **Force Burst** button to align a burst on demand, and a **target-agreement** line showing whether your DPS are focused or split and who's off-target

### Fix — Samurai: throws Enpi when knocked out of melee instead of idling
- When Samurai was pushed out of melee range (knockback, a mechanic, or repositioning) it just stood there doing nothing until it walked back in, dropping GCDs the whole time. It now throws Enpi to keep the GCD rolling until it's back in melee — the same ranged-filler behavior Ninja already has with Throwing Dagger, and it uses the enhanced Enpi proc (from Hissatsu: Yaten) when that's up. In melee nothing changes: the check is position-based so it never diverts a real combo to Enpi while you're in range

## v0.1.10 — 2026-07-07

### Fix — Sage: Kardia now lands on a real-player tank (multibox/party)
- Kardia would get stuck on the Sage and never move onto a live party tank — it worked in Trust but silently failed with a real tank. The two status IDs Kardia uses to tell "is Kardia on me" apart from "is Kardion on my target" were swapped, so when Kardia sat on the Sage the rotation wrongly concluded it was already on the tank and suppressed every placement. Corrected the IDs; Kardia now tracks and places on the tank the same way in parties as it does in Trust

## v0.1.9 — 2026-07-05

### New — low-level Gladiator/Marauder: walk to mobs you can't reach (pre-Lv15)
- Below level 15 a tank has no ranged attack (Shield Lob/Tomahawk unlock at 15) and no gap closer — so a Gladiator facing a parked ranged or caster mob just stood there with no way to gather it. Tanks now walk into melee on the engage target when they have no ranged tool, using the same range-keeping movement melee jobs use (walk-in only, yields to BossMod, respects the vNav flex dead-band). At 15+ nothing changes — the ranged attack gathers as before. Toggle in Control → Tank ("Walk To Target (pre-Lv15)", on by default); Why Stuck names the state while walking

### Fix — Summoner: Crimson Strike was never used (Lv86+)
- Crimson Cyclone's melee follow-up was missing from the rotation entirely — every Ifrit phase dashed in with Cyclone and walked away leaving the free instant unspent (caught in the first SMN validation run). Crimson Strike now follows Cyclone whenever the Crimson Strike Ready buff is up and the target is in melee reach; if a knockback throws you out after the dash, the follow-up waits until you're back in range instead of jamming the rotation. It also fires the very GCD after the dash (the only one guaranteed in melee) instead of letting a Ruby Rite cut in line

### Fix — Summoner: demi summon no longer fires mid-primal, and Phoenix is never mistaken for Bahamut
- Right after summoning a primal there's a 1–3 second gap before the pet lands where the gauge reads "nothing to do" — the demi summon fired into that gap, burying Phoenix inside the Garuda window it had just opened (Brand of Purgatory interleaving Emerald Rites in the validation logs). The demi entry now waits out the pet arrival
- The demi-type detection latched once at summon start and a wrong read never corrected itself — one window submitted Enkindle Bahamut and Deathflare while the game was actually in Phoenix, losing the Rekindle heal. Detection now re-checks the Astral Flow button live every frame and corrects itself the moment it names a phase

### Fix — Ninja: Rabbit Medium at pull open + honest uptime numbers
- Two Rabbit Mediums in three level-60 pulls, both at pull start: a hand sign could submit 0.3 seconds after the previous one (the 0.5s mudra recast sits inside the queue window, so the "GCD ready" check passes almost immediately), racing the game's combo tracking into an invalid sign. Signs now pace at the real mudra recast — clean sequences were already spacing 0.7–0.8s, so nothing slows down
- Kassatsu could weave in *between* hand signs, which flips the state the sign resolver reads mid-sequence and derails the remaining signs (the second Rabbit's cause). It now waits for the sequence to finish — the next window picks it up and the enhanced ninjutsu follows as intended
- The uptime counter credited each mudra sign only its 0.5s recast, though signs physically can't chain faster than ~0.75s — frame-tight Ninja play read as 75–83% "uptime" and sent people bug-hunting. Sub-second GCDs now credit their real achievable cadence, so Ninja percentages finally mean what they say

### Fix — Ninja: Trick Attack no longer burned on dying packs
- Trick Attack / Kunai's Bane kept landing on mobs seconds from death at the end of a pull — a +10% window on a dying pack wastes the 60-second cooldown. The rotation now estimates the pack's time-to-kill from the recent damage rate (the same logic that holds the Machinist Queen) and holds the burst when the pack is about to melt; Shadow Walker lasts 20 seconds, so the buff rides through the transit and Trick opens the **next** pack at full value instead. Tunable in Ninja config ("Trick Min Pack TTK", default 6s, 0 disables)
- A second guard covers what the damage-rate estimate can't see: after a boss add-phase (boss untargetable, damage flat) the estimate resets, and Trick landed on a boss at 0.25% HP one second before the kill — Trick now also holds when the target itself is nearly dead ("Trick Hold Target HP", default 5%, 0 disables)

### Fix — Dragoon: combo died at step 2 from level 96 (whole dungeons of True Thrust → Disembowel)
- At level 96 the second combo hits upgrade — Disembowel becomes Spiral Blow and Vorpal Thrust becomes Lance Barrage — and the game reports the upgraded id in the combo state. The combo tracking didn't know the new ids, so from 96 on every combo reset after the second hit: no Chaotic Spring, no Heavens' Thrust, no positionals, no Drakesbane, entire dungeons of two-hit combos (caught at Lv100, 46–74% uptime). Both upgraded ids now drive the combo everywhere they're read, including Life Surge's "big hit next" timing. Same class of bug as the earlier 4th/5th-hit fix, one step earlier in the chain

## v0.1.8 — 2026-07-05

### New — Boundary camping: melee positionals become a short hop (experimental, OFF by default)
- Opt-in via Nav Control → "Boundary Camping (experimental)": melee stands just inside the required flank/rear zone, right next to the 135° boundary between them — so when the rotation swaps from a flank positional to a rear one (Armor Crush ↔ Aeolian Edge), the character hops ~2 yalms across the line instead of running a quarter circle around the target. A bias slider (0–30°, default 10°) tunes how far inside the zone to stand
- With Auto-Manage BMR AI on, BossMod is told "any positional" for melee while camping is active — BossMod keeps range and dodges, Daedalus owns the standing angle. The hop may run while BossMod's AI is merely follow-steering, never during live danger
- Ninja is the pilot job; other melee stay on their current movement until validated. Ships OFF while the movement cadence is field-tested — leave it off if you see any movement misbehavior

### Fix — Dropped GCDs recover ~3× faster (knockback-heavy bosses)
- When the game accepts a queued weaponskill but then drops it at fire time — a knockback or the boss strafing out of reach at the exact rollover instant — the rotation used to sit on the dead queue for up to 0.6 seconds before retrying. It now detects the drop within a tenth of a second of the GCD reading Ready and resubmits. On knockback-heavy bosses this was worth several percent of uptime ("submitted but not cast" entries in the debug log are this event)

### Fix — Smoother chasing of moving packs
- While chasing a pack that's running to its camp, movement re-pathed up to three times a second toward destinations centimeters apart — visible micro-jerks. The arbiter now remembers where it last sent you for a second and skips near-identical re-paths; genuine direction changes still go through instantly

### Fix — Movement no longer fights BossMod (screen stutter during dodges)
- Daedalus pathing and BossMod's AI dodging were steering the character at the same time: BossMod pauses itself whenever a vnavmesh path runs, so every short Daedalus move interrupted the dodge mid-step, then BossMod snapped back the moment the path ended — a visible tug-of-war stutter near AoE telegraphs. A new movement arbiter now yields the character to BossMod whenever it's dodging or danger zones are up (plus a short cooldown after they clear), and rate-limits Daedalus's own path submissions so no movement source can machine-gun vnavmesh. Kill-switch in Nav Control ("Yield movement to BossMod", on by default), with a live status line showing who owns movement and why pathing is held
- Casters now also hold hard-casts while BossMod's AI is actively steering — its input-injection movement was invisible to the "am I moving" check, so casts could start mid-dodge and get interrupted

### Fix — Ninja: removed the special burst movement (the worst stutter case)
- Ninja was the only job with a second movement driver — a dedicated "burst approach" that walked into melee during Suiton prep and canceled the regular positioning path to do it. At the melee boundary the two drivers flip-flopped every frame (cancel, re-path, cancel), the exact stutter loop players saw. The burst approach is gone; Ninja now uses the same max-melee range keeping as every other melee job, which already walks into range for Kunai's Bane (requires "Maintain max melee", on by default). The "Burst Melee Approach" toggle is removed

### Fix — Black Mage: rotation idled for a minute+ "Waiting for MP"
- Umbral Ice no longer restores MP over time on the current patch — the refill happens when you **cast** an ice spell (a Blizzard or Blizzard IV at Umbral Ice III refills the whole bar). The ice phase still waited passively, so a pull entered with 3 leftover Umbral Hearts (Blizzard IV skipped) and a Despair-emptied bar just stood there while natural regen crawled — 60–90 second freezes in Origenics, one pull at 21% uptime. The ice phase now casts Blizzard IV (Blizzard below Lv58, Freeze in AoE) to trigger the refill instead of waiting
- The "go back to fire" gate also demanded a 99% bar — one regen tick more than a stalled bar ever reaches ("Waiting for MP (98%)" for 24s+). It now transitions at 9600 MP, matching RSR

### Fix — abilities no longer starve while the rotation is stalled (all jobs)
- Off-GCD abilities only fire in the weave window during a rolling GCD — so whenever the GCD chain stalled (like the Black Mage MP freeze), every ability starved with it: Lucid Dreaming couldn't fire to fix the very MP shortage causing the stall, and defensives sat idle too. If the GCD sits unused for 1.5 seconds, the weave window now opens so abilities keep flowing during a stall

## v0.1.7 — 2026-07-04

### Fix — Bard: DoTs no longer allowed to fall off before Iron Jaws (below Lv56)
- Windbite and Venomous Bite were only recast after the DoT fully dropped, which in practice meant 3–8 seconds of DoT downtime every 45-second cycle (detection lag plus queue position). Below Iron Jaws the recast IS the refresh — it now fires when 3 seconds remain, keeping both DoTs rolling continuously through the leveling range. Caught in a Lv49 Aurum Vale log

### Fix — Melee sometimes refused to walk into range (all melee jobs)
- The walk-into-melee movement resolved its target through checks that ask the game "can I attack this?" — a probe that falsely says no while the mob is still out of range, which is exactly when you need to walk in. When it misfired, the character just stood there (with BossMod AI enabled its movement masked the gap, which made it look intermittent). Movement now falls back to your actual hard target with a probe-free check — a live, targetable, hostile mob is enough to walk toward. Applies to max-melee range keeping on every melee job, plus Ninja's burst approach. Combat targeting keeps the probe (it's what stops attacks on story allies)

### Fix — Black Mage: rotation froze trying to hard-cast Thunder without its proc
- Every Thunder spell now requires the Thunderhead proc, but the ice phase kept an old "hard cast Thunder" fallback for when the DoT lapsed without a proc — the game refused every attempt ("Cannot use yet") and the rotation sat on it, freezing the GCD until the next phase swap granted a proc. Caught live in The Aitiascope ("Stuck — Thunder IV: ActionStatus 572"). The fallback is gone; the DoT now waits for the next Thunderhead and fillers carry the rotation. All six Thunder variants also gained a dispatch-level proc gate so nothing similar can slip through again, and refusal code 572 now shows a plain-word label in Why Stuck

### Fix — Debug window: opening it mid-duty showed frozen "Idle" state
- The flag that turns on per-frame debug-state reporting lived on the saved settings object, but rotations run on a snapshot copy that only refreshes on zone or settings changes — so opening the Debug window mid-dungeon showed factory defaults (DPS State "Idle", Target "None", Engaged 0) for every job until something happened to refresh the snapshot. Same root cause as the v0.1.3 automation on-switch fix; the flag is now process-wide and the window reports live state the moment it opens

### Fix — Dragoon: Rise of the Dragon watched the wrong buff (Lv92+)
- The Dragonfire Dive follow-up was gated on Draconian Fire — the combo buff from your 5th combo hit — instead of Dragon's Flight, the buff Dragonfire Dive actually grants. The follow-up only fired when your combo position happened to line up with the dive, wasting part of every 2-minute window. Found during the Lv90 validation run, fixed before it ever went live at 92. The stale Life of the Dragon status id was corrected in the same pass (RSR-verified)

## v0.1.6 — 2026-07-04

### Fix — Parser: DoT damage was invisible (the game moved the tick channel)
- DoT ticks never reached the parser — the packet category the whole community documents for them (and that parsers like ACT read off the network) now only carries out-of-combat regen inside the game client. A live packet-capture session found the real channel, and it's better than the old one: the game reports each tick **per caster**, so your Biolysis/Dia/Thunder damage lands on your row exactly — no estimating. In the validation run, a quarter of Scholar's damage had been missing from the meter
- Row tooltips show the DoT share explicitly ("· DoT 132.1k"), and the parser's (?) tooltip now reports the tick pipeline's health — if a future game patch moves the channel again, it says so right there instead of silently undercounting
- For the curious (or the next patch): a "Raw packets" toggle in the Debug window's Debug Log tab dumps the raw combat packet stream — off by default, it's a firehose

### Fix — Scholar: stop hoarding an Aetherflow stack in dungeons
- Scholar always held one Aetherflow stack back "for emergencies", which is trial/raid tuning — in dungeon content it just meant one fewer Energy Drain per cycle, every cycle. Dungeons now spend every stack (Energy Drain damage beats a hypothetical emergency Lustrate that trash packs never need); trials keep 1 in reserve and high-end keeps 2, and your own saved value still wins if you've set one

### Fix — melee no longer freezes staring at a story ally
- Duty NPCs that walk with you through story dungeons (and quest-battle allies) could be picked as melee targets — the rotation would path to them, face them, and stall for 10+ seconds until a real enemy wandered close, because the game refuses every attack on them. All enemy scans now run the same attackability probe the targeting service already used elsewhere. Caught as repeated 14-second Dragoon idle windows in Trust runs

### Diagnostics — "why is it stuck" now names the exact refusal
- When an action can't fire, the Why Stuck tab shows the game's raw refusal code with a plain-word label (out of range, line of sight, not facing, not unlocked, GCD rolling…) instead of the bare word "ActionStatus"

## v0.1.5 — 2026-07-04

### New — Farm mode (work in progress)
- Grind an item from specific mobs, hands-free: pick the item (search by name), a target count, the mobs, and one or more farm spots — then Start. Daedalus kills your mobs around the spots, roams between them while waiting for respawns, and stops with a chat summary the moment the target count is in your bag. Everything stays in one zone — no teleporting
- **Pulls like a player**: walks to ranged tag distance, stands still, tags the mob, and kills it while it runs in — melee finishes on arrival, and anything that aggroed you gets cleared before the next pull. Kits with no ranged attack walk to melee after a short tag window
- **"Find droppers"**: after picking the item, one click looks up which mobs drop it (GarlandTools — the same data Monster Loot Hunter shows) with level and zone. Clicking a dropper adds it to your kill list, **flags its spawn on your map** (any zone), and adds a farm spot automatically when it lives in your current zone. Manual setup still works: target a mob → "Add current target", stand at the spawns → "Add spot (my position)"
- Enable the Farm button under Settings → General → Farm ("Show Farm button on main window"); a green dot marks an active run. Requires vnavmesh for movement
- The mob/spot list is session-only for now (not saved on logout) — saved farm profiles are planned next (see docs/farm-mode.md)

### Fix — Scholar: Energy Drain finally flows (gauge was read from the wrong memory)
- The Aetherflow stack reader (and the Fairy Gauge reader) read raw bytes at the wrong offset of the job gauge — the count was garbage, so Energy Drain never fired and Aetherflow/Fey Union decisions ran on wrong numbers. Both now read the proper gauge fields. In a dungeon run this alone is roughly 9-10 Energy Drains per minute of Aetherflow cycle that Scholar was leaving on the table
- Dungeon tuning: Art of War now requires 3 enemies (2 hits = 280 potency vs Broil's 310 — a pure loss with no utility rider), and Sage's Dyskrasia likewise stays at 3 (340 vs 380). Astrologian keeps its 2-target AoE (Gravity actually beats Fall Malefic there) and White Mage keeps 2 for Holy's stun

### Fix — Bard: Quick Nock / Ladonsbite were fired at yourself
- The AoE filler was dispatched at the player, but Quick Nock and Ladonsbite are targeted 12y cones (they require an enemy target) — the game refused every attempt ("invalid target" spam) and packs fell back to single-target shots. The cone now fires at the current enemy like Wide Volley/Shadowbite already did. This was costing Bard its entire AoE filler in every pack since the AoE rotation shipped

## v0.1.4 — 2026-07-04

### New — Questionable kill quests work without any combat module
- Questionable only targets kill-quest mobs when one of its combat modules (RSR/Wrath/BossMod) is configured — without one it walks to the objective and waits forever. Daedalus now reads Questionable's quest-step data directly: while a kill step is active it starts the rotation AND picks the targets itself — enemies already attacking you first, then **only mobs the game has flagged with the gold quest icon** (the same marker you see over objective mobs), so unrelated camps never get aggroed. Next mob when one dies, stopping the moment the step completes. Zero Questionable configuration needed; if you do set its combat module to "Rotation Solver Reborn", its exact quest-mob targeting takes over seamlessly
- **Aggro cleanup**: leftovers that are still hitting you after the kill objective completes — or mobs that aggro while Questionable walks you through a camp — get killed before the rotation releases, instead of following you across the map as a train
- **Flagged mobs are a standing kill order**: kill objectives don't always line up with the quest step Questionable reports (it can sit at "step completed" with 4/8 mobs still marked). While Questionable is running, any gold-icon mob in range now gets killed, one at a time, until no icons remain

### Fix — no more wasted DoTs on dying enemies (all jobs)
- Caught in a Scholar log: Biolysis applied to a mob at 4,550 HP that died two casts later — the DoT never ticks enough to beat just casting filler. DoT application now checks the target's estimated time-to-kill (RSR parity) and skips enemies about to die; fresh pulls are never skipped since their time-to-kill is unknown. Applies to every DoT that uses smart target selection — healer DoTs, Bard's Iron Jaws spread, Blue Mage bleeds. New toggle + threshold under Settings → General → Targeting ("Skip DoTs on dying enemies", default on, 10s)

### Fix — Scholar: Summon Seraph no longer burned at full health
- The default Seraph strategy was "on cooldown", so it fired seconds into a pull with nobody hurt. New default is "save for damage" (fires when average party HP drops below the Seraph threshold), and existing configs are migrated automatically — if you deliberately want on-cooldown Seraph, re-pick it once under Scholar settings and it sticks

### Fix — AoE spells no longer flicker to single-target mid-pack (all jobs)
- Caught in a Scholar log: Art of War hitting 5 enemies, then a lone hardcast Broil, then Art of War again — repeating. The AoE enemy count ran a line-of-sight raycast against every mob, which flickers at pack edges over uneven dungeon terrain, momentarily dropping the count below the AoE threshold. Point-blank AoE isn't blocked by bodies or props, so the hit count no longer raycasts (targeted-AoE counting already worked this way) — mid-pack AoE stays AoE

## v0.1.3 — 2026-07-04

### New — Automation bridges, round two: AutoDuty + auto-engage
- **AutoDuty** runs now auto-start the rotation — no more manually enabling Daedalus before a farming session. This also covers Henchman's duty hunt-log marks ("Solo Unsync Duty" / duty-support dungeons): Henchman hands those dungeons to AutoDuty, so Daedalus follows AutoDuty's running state and fights inside the instance
- **Automation now opens on passive marks**: hunt-log and quest mobs usually won't attack first, and Daedalus previously waited for combat to start — so Henchman would target a mark and both plugins would stare at it forever. While automation is driving, a live attackable hard target now counts as "engage": Daedalus fires the opener the moment the driver targets the mob (and waits politely while you're mounted). Manual play is unchanged — Daedalus still never pulls on its own
- The main window and overlay now show **who's driving** in gold next to the duty label — "Dungeon · Henchman" (or AutoDuty / Quest) — and the Debug window's Why Stuck tab gained an "Automation" line (override held? engaged? waiting on what?) plus a build stamp in the header

### Fix — the v0.1.2 bridges could switch Daedalus on but not make it act
- Rotations read an internal snapshot of the settings (the duty-tuning overlay), and the automation on-switch never reached that snapshot — so Questionable/Henchman would report the rotation "started" while every module still saw itself as off. The switch is now visible to the rotation the same frame it flips. Validated end-to-end on overworld hunt-log farming: marks get targeted, opened on, and killed, and the task advances on its own

## v0.1.2 — 2026-07-04

### New — Questionable & Henchman automation bridges
- Daedalus can now be driven by the **Questionable** quest plugin for kill quests. Set Questionable's combat module to "Rotation Solver Reborn" — Daedalus answers the same plugin-to-plugin calls, so Questionable targets the quest mobs and starts/stops the rotation around each fight automatically. No setup in Daedalus needed; your Enable switch and saved settings are untouched (automation-driven combat shows as "Enabled (Auto)" on the main window)
- **Henchman** hunt farming works too: while a Henchman task is running (BumpOnALog hunt logs, OnYourMark hunt bills, Bring Your A/B Game rank farming), Daedalus runs the rotation automatically — Henchman targets each mark and handles the travel, Daedalus does the killing. Rotation starts when the task starts and stops when it finishes; no Henchman configuration needed (its "Auto Rotation Plugin" setting can stay on anything)
- Safety: if automated combat somehow lands on a striking dummy, Daedalus drops the target and stops the automation-driven rotation instead of hitting the dummy forever (manual dummy practice is unaffected)
- If the real Rotation Solver Reborn plugin is loaded, Daedalus steps aside and leaves the quest integration to it

### Fix — Scholar: Fey Union guard checked a boss status
- The "is a tether already running" check read status 1224 on the scholar — which is **Earthly Dominance**, a boss status that never appears on a player — so it never fired and Aetherpact could be re-pushed while a tether was live. The guard now checks the real Fey Union status on the scholar plus the RSR-style scan of party members for the tether

### Fix — Dragoon: the missing 4th and 5th combo hits (Lv56+)
- Fang and Claw, Wheeling Thrust, and Drakesbane **never fired at any level** — every combo restarted after 3 hits (caught in a Lv68 boss log where the toon clearly had them). They were gated on the Fang-and-Claw-Bared / Wheel-in-Motion proc buffs, which Dawntrail removed when it turned these into plain combo continuations. The steps are now driven by the game's combo state: Full/Heavens' Thrust → Fang and Claw (flank), Chaos Thrust/Chaotic Spring → Wheeling Thrust (rear), either → Drakesbane at 64+ (which was also mislabeled as a "Lv92 replacement" — it's the Lv64 fifth hit). Both positionals are job-quest locked, so unlearned skills fall through to a combo restart instead of stalling. This is a large DPS gain at every level from 56 to 100
- Life Surge's "big hit next" detection updated to match: it now correctly saves the guaranteed crit for Drakesbane or Heavens' Thrust

## v0.1.1 — 2026-07-03

### New — Built-in DPS parser
- Daedalus now has its own parser — a **Parser** button on the main window opens an ACT-style damage meter that tracks **everyone**: your toons, other players (tagged HUMAN), and Trust/duty-support allies (tagged TRUST), with pet and summon damage merged into their owner's row. Rows show job, name, DPS, and damage share on a proportional bar; your own toon is highlighted in gold. Hover any row for total damage, crit%, and direct-hit%; the header shows the boss, fight timer, and party DPS, and a dropdown keeps your recent fights (configurable history)
- **Borderless mode** (Settings → General → Parser) turns it into a compact semi-transparent overlay — bars, first names, and share % only — with optional click-through. A hide-out-of-combat toggle applies to both window modes, and a name scramble toggle swaps character names for mythological aliases for streaming
- **DoT ticks now count** — the parser hooks the game's tick packet stream (ticks never appear in the normal damage events, which is why every hook-based meter undercounts DoT jobs by default) and attributes each tick to whoever applied the effect: directly when the packet names the source, otherwise via the ticking debuff on the target. Ticks that can't be attributed are dropped, never guessed, and tick damage doesn't dilute crit/direct-hit rates. Chaos Thrust, Dia, Higanbana and friends finally show up in the numbers
- **Cross-toon exact numbers over IPC/LAN**: every Daedalus toon now broadcasts its own parse (which is exact — you always see your own hits) every 2 seconds in combat, plus a final report when the fight ends. Toons receiving a report flip that row to a green dot, drop the HUMAN tag, and use the reported damage, crit%, and direct-hit% instead of the locally-observed numbers — so an 8-toon party shows everyone's real parse even when someone was out of packet range. Reports only merge from toons **in your party and zone** — a toon grinding a different duty broadcasts too, but its numbers can't bleed into your fight. Phase cutscenes that drop the combat flag (Porta Decumana's transition) no longer split one fight into two encounters, and every displayed number (row DPS, damage share, party DPS) runs on one clock so they always reconcile. On by default when the LAN coordinator is enabled; toggle under Settings → General → Parser ("Share my DPS over IPC/LAN")
- Accuracy fix along the way: damage and heal values over 65,535 were being truncated in the damage tracking hook (they're split across fields in the game's wire format). Big hits — routine at level 100 — now decode fully, which also corrects the personal DPS figure in Analytics

### Fix — Bard: leveling-range audit (Archer through 83)
- **Wide Volley was never used**: the Lv25 AoE Hawk's Eye spender (the pre-72 Shadowbite) existed in the data but nothing referenced it, so on packs from 25–71 every proc was spent on the single-target shot. Packs now use Wide Volley, upgrading to Shadowbite at 72
- **Bloodletter charges wasted below 84**: the overcap dump was hardcoded to 3 charges, but the cap is 2 until the Lv84 trait — outside Mage's Ballad and Raging Strikes the charges just sat full for the entire leveling range. The dump now uses the real charge cap for your level
- Resilience: the AoE filler no longer suppresses the single-target filler fallback, so a rejected Quick Nock/Ladonsbite can never stall the GCD

### Fix — Dragoon: Life Surge no longer wasted on Piercing Talon
- Caught in a Porta Decumana log: Life Surge was weaved after Vorpal Thrust while the boss was out of melee reach, so the guaranteed crit landed on Piercing Talon — the weakest hit in the kit — instead of the combo finisher. Life Surge now holds until the target is back in melee range

### Fix — Dancer: dance partner no longer flaps mid-pack
- Closed Position randomly dropped for a GCD then re-applied — sometimes swapping from the RDM avatar to the tank and back — with nobody dead. Root cause: the ally scan's "hostile" filter was actually testing the **is-casting** flag, so any trust ally mid-hardcast (the RDM casts constantly in packs) vanished from the party for a moment, the Dancer concluded her partner had left, and re-partnering while partnered executes Ending (that's the drop). Follow-up field test showed the real hostile flag is no better — the game sets it on trust allies **while they fight**, which made in-combat avatars vanish and the parser merge their damage into the player's row. The ally scan no longer trusts NPC status flags at all (the game's Trust role marker is the reliable test), and as insurance, automatic re-partnering now requires the "better partner available" condition to hold for 3 full seconds before acting — a scan hiccup can never cost an Ending again

### Fix — Dancer: Closed Position finally works with Trusts
- In Trust and Duty Support content the game reports an empty party, so the Dancer concluded she was solo and never applied Closed Position — a straight damage loss in every trust dungeon. Partner selection now finds Trust allies the same way the healers do, so a dance partner is applied on zone-in; avatars that don't report a job are still taken as last-resort partners (Standard Finish buffs you regardless of who holds it)

### New — Overlay: mechanic forecast everywhere BossMod goes
- The overlay's RAIDWIDE / TANKBUSTER countdown previously appeared only in the handful of savage and ultimate fights with a bundled timeline. It now falls back to BossMod Reborn's live timeline whenever BMR has a module for the current duty — trials, raids, dungeons — showing the boss name plus urgency-colored countdowns to the next raidwide and tank buster. Fully automatic: bundled timelines (with mechanic names) still take priority, and nothing changes if BMR isn't installed
- The overlay status pill now matches the intended design — a rounded chip reading "● Running" / "● Paused" (was a plain ACTIVE/INACTIVE button); still click-to-toggle

### New — The Daedalus look: UI overhaul (goodbye Olympus grey)
- Every core window now wears the Daedalus identity — dark layered panels with gold accents, status colors reserved for actual status. Design system lives in the repo (`docs/ui-mockups/` has browser-viewable mockups of every window)
- **Main window**: status row with live GCD uptime from the last fight, rotation codename in gold with the current duty underneath, and the Enable/Disable master switch as the one gold-filled button in the plugin. The LAN Party button shows a green presence dot while peers are connected; version moved to the footer
- **Overlay**: semi-transparent, status pill toggle (Running/Paused), next action in gold, HP and injured-party on one line, and the mechanics forecast now uses full severity tags (RAIDWIDE / TANKBUSTER / ENRAGE) with urgency-colored countdowns — the nearest mechanic stays bright, later rows dim
- **Party Coordination**: machine headers show the actual hostname; each toon's role icon (T/H/D chip) doubles as sync health — filled green when synced, yellow when the heartbeat runs late, red when the connection drops, hollow while negotiating — with the reason spelled out beside the HP bar and a (?) legend tooltip
- **Settings**: the sidebar's old blue selection is replaced by a gold accent bar over a soft gold wash, group headers are dimmed for hierarchy, and every job section opens with its name, Greek codename, and an honest validation chip (validated / pending / untested)

### New — LAN Coordinator: cross-machine party coordination
- Daedalus instances on **different PCs** can now coordinate over the local network (UDP broadcast, same VLAN — no router setup). Same-machine toons keep using the existing low-latency IPC; a new coordination bus mirrors all party-coordination traffic across both transports and deduplicates automatically, so nothing double-processes. Opt-in under Party Coordination settings: enable toggle, port (default 47200), machine ID display and a live status indicator — remember to allow the UDP port through Windows Firewall on every machine
- **New Party Coordination window** (appears while LAN is enabled): every toon across all machines grouped per machine with online dot, name, job, HP bar and assigned role slot — fed by 2-second heartbeats (grey after 5s silence, dropped after 30s), with peer count and latency in the status bar. **Scramble Names** swaps character names for mythological aliases (stream/screenshot safe, display-only), plus HP-bar, remote-only and compact toggles, and a Copy Party Data button
- Zone-in **role negotiation** (tanks/healers/DPS slotted identically on every machine), **coordinated burst** plumbing (BurstReady/BurstFire opens the existing burst window on all toons simultaneously), and a **healer-down detector** that broadcasts when every healer in the party is dead — the Phoenix Down carrier hook listens on the bus (item execution lands in a follow-up)

### New — Blue Mage: loadout awareness + role checklist
- Proteus now reads your **active 24-slot spell set**, so availability is truly "learned AND slotted": the rotation skips spells you own but didn't slot instead of pushing casts the game rejects (falls back safely to learned-only if the read is unavailable). The Blue Mage debug tab shows the live count ("22/24 slotted")
- The Missing window gains **role loadout checklists** (Blue Academy reference sets, patch 7.5): collapsible Tank / DPS / Healer headers with a per-spell verdict — ✔ slotted, ● learned but not slotted, ✗ not learned with its farm source — split into Core (the role doesn't function without it) and Flex (content-dependent alternates), plus a summary per role so you can see at a glance what to slot or farm before queueing

### New — Blue Mage support (Proteus)
- Daedalus now plays Blue Mage. Because BLU has no fixed role, a **Role dropdown** (DPS / Tank / Healer) in the new Blue Mage config section drives everything: **Aetheric Mimicry is applied automatically** by scanning your party (players and Trust NPCs) for someone matching your chosen role — reapplied after death or a role change; Tank role maintains **Mighty Guard** (dropped automatically when you switch roles) and fires **Diamondback** below a configurable HP threshold; Healer role handles **White Wind** thresholds. The damage rotation covers Song of Torment DoT upkeep, The Rose of Destruction on cooldown, Plaincracker AoE (counted around *you*, never a distant pack), Sonic Boom filler with Goblin Punch as the tank/movement filler — and spells missing from your loadout simply fall through to the next choice instead of stalling
- The **Missing window doubles as a spell-hunting planner** on BLU: all 124 spells are tracked, and every unlearned spell shows exactly where it's learned (duty, open-world zone, or Whalaqee Totem) — data pulled from the game's own Blue Magic spellbook sheets

### Fix — LAN: same-machine toons no longer show as separate "machines"
- The Party Coordination window listed a second toon on the same PC as "Machine 2 (Remote)" — each game instance was generating its own random machine identity. The identity is now the computer's hostname, so all toons on one PC group under the local machine and the peer count only counts actual other machines
- The latency figure was inflated by up to a full heartbeat interval (2s) of queueing time on the echoing side — 981 ms shown on the same PC. Heartbeats now report how long the echo was held so the displayed number is actual round-trip time

### New — Physical ranged defensives: Second Wind, Shield Samba / Troubadour / Tactician
- Bard, Machinist and Dancer never used their defensives at all. All three now fire Second Wind below 50% HP, and their party mitigation (Shield Samba / Troubadour / Tactician) on big pulls (3+ engaged enemies, configurable) — skipped automatically when another ranged toon's mitigation is already up, since the three buffs don't stack. Dancer also gets Curing Waltz below 60% HP. Toggles under the shared ranged settings

### Fix — Low-level rotation audit (all jobs): no more dead ends while leveling
- **Quest-locked skills count too:** a level 41 Dragoon with the Doom Spike job quest undone stalled completely on any 3+ pack — the skill was level-met, so the rotation pushed it forever while the game refused it. AoE gates on DRG/NIN/MNK/WAR now require the skill to be actually unlocked, not just level-met (the Missing window tells you which quests to finish)
- After the Dragoon find, every job was audited for the same low-level traps. Three more found and fixed:
- **Ninja (30-37):** an AoE pack pushed Death Blossom — which unlocks at 38 — forever, and nothing else. Packs below 38 now run the single-target combo
- **Monk (below 26):** the Opo-opo form in AoE pushed Arm of the Destroyer before it's learned; it now falls back to Bootshine like the other forms already did
- **Warrior (below 26):** every completed Maim tried to push a combo finisher that doesn't exist yet (caught by the Heavy Swing fallback, but pure rejection noise every chain) — the finisher branch now knows there is no step 3 below Storm's Path

### Fix — Dragoon combo can always restart (low-level stalls fixed)
- Dragoon gains its ranged filler: Piercing Talon now fires when you're genuinely out of melee reach (forced disengages, mechanics), keeping the GCD rolling instead of dropping to nothing — and never in melee, since it resets the combo. Toggle in the Dragoon config (on by default)
- From level 18 to 49, every combo went into Disembowel: the "DoT needs refreshing" check didn't know the DoT only exists once Chaos Thrust unlocks at 50, so it always demanded the Disembowel line. Now Disembowel fires only when Power Surge actually needs refreshing at those levels, and the rest of the combos run the harder-hitting Vorpal Thrust line
- A low-level Lancer whose combo state said "finisher next" (easy to carry between quick open-world kills — the combo timer is 30 seconds) pushed a finisher it doesn't have yet, forever — the rotation sat dead until the target died (20-45% uptime pulls). Combo steps are now backed by a True Thrust restart whenever the step can't actually fire, finishers are properly gated below their level, and below Doom Spike a 3+ pack runs the single-target combo instead of nothing

### Fix — Dancer actually dances now
- …but not in town: the pre-pull dance and partner buff no longer fire inside sanctuaries (Limsa, Ul'dah, aetheryte camps) — only Peloton remains active out of combat there
- Standard Step and Technical Step never fired — at all, ever. They're weaponskills (they roll the GCD) but were being dispatched as abilities into weave slots, where the game always refuses them. Both now dispatch as the GCDs they are: Standard Step on cooldown for the damage buff, Technical Step for the raid burst window
- While mid-dance, the rotation no longer tries to fire Fan Dances and cooldowns the game locks out during steps
- Dance partner: an unrecognized job could never be picked at all (rather than being picked last) — the dancer sat partnerless in some parties. Anyone is better than no one, since your own Standard Finish buff doesn't depend on who the partner is

### Fix — Red Mage burst chain no longer deadlocks in dungeons
- Manafication never fired in solo/dungeon play: Embolden went out on its own, and Manafication then waited for an Embolden cooldown that couldn't come back in time — every pull, all pull. Worse, the melee combo was parked through the live Embolden window waiting for that missing Manafication, so Riposte → Zwerchhau → Redoublement and all three finishers landed *after* the buff expired, and Prefulgence (which needs Manafication) never fired at all. The chain now fires in order — Manafication first, Embolden right behind it, combo inside both — and every hold has an escape: if one cooldown is desynced or disabled, the other fires on its own instead of drifting
- The AoE melee chain (Enchanted Moulinet ×3) was interrupted by a weak, uncombo'd Zwerchhau after the first swing: Moulinet grants a Mana Stack while keeping the combo timer alive, which the combo tracker misread as the single-target chain being in progress. Moulinet chains now run clean through to the Verflare/Verholy finisher
- Once Manafication fires, Embolden now always follows it (and vice versa) — previously the follower re-checked pack size, so if a mob died in the one GCD between them, Embolden was held back "for the pack" for the rest of the fight and Manafication burned alone
- Single-target pulls (and bosses) now burst: the solo burst gate required 2+ enemies near the target, so a lone big mob or a boss never saw Manafication or Embolden at all. A lone target now qualifies whenever it's projected to outlive the burst window at the current kill rate (configurable, default 10s)
- Embolden no longer trails Manafication by 15+ seconds during hardcast-heavy stretches: Fleche, Contre Sixte and Prefulgence now leave the scarce weave slots free while Embolden is queued to follow — which also means Prefulgence lands *inside* Embolden instead of before it
- The Moulinet AoE chain now actually chains (Moulinet → Deux → Trois): the combo-progress probe was aimed at the Enchanted Moulinet id, which never morphs — only the base hotbar button does. Every AoE melee GCD was restarting the chain at hit 1
- The action log no longer shows phantom duplicate casts at impossible sub-GCD spacing (e.g. three Verstones inside 1.8 seconds): the "new GCD fired" detector now requires the recast timer to actually restart, which also stops recast jitter from spuriously resetting the weave counter
- Prefulgence now outranks Fleche and Contre Sixte for the weave slot — after a burst there are only a couple of weave openings, and the 900-potency Embolden-window nuke was losing them to filler oGCDs and timing out with the pull
- Engagement no longer fires phantom casts from caster range: the 3-yalm melee oGCD was being pressed while standing at spell range (Manafication extends the sword swings to 25 yalms, but not Engagement) — the game quietly accepted and dropped each press without spending a charge, so it repeated every weave slot, hitting nothing and crowding out real abilities. It now only fires from actual melee range
- Manafication's free melee combo now actually happens: Magicked Swordplay (3 cost-free enchanted melee GCDs) wasn't modeled, so when Manafication fired below 50|50 mana the combo entry stayed locked and the whole buff window ran out on hardcasts. Combo entry now opens whenever Magicked Swordplay is up, and the gap-closer dash is skipped during Manafication since the enchanted swings reach 25 yalms on their own

### Fix — AoE mode no longer sticks on spread packs (all jobs)
- The "how many enemies would this AoE hit" count around a target was inflated by the target's own hitbox — on large mobs that made two enemies standing well apart look like a stacked pack, so casters kept pushing AoE spells that only ever hit one target (seen as 27 straight seconds of single-target Impact hits on Red Mage in The Vanguard). The count now matches how the game actually resolves target-circle AoEs, so spread packs correctly fall back to the single-target rotation

### Fix — White Mage no longer casts Cure I past level 30
- Cure I was sneaking into high-level play through two holes: an MP-conservation mode that picked it to "fish for Freecure" (a proc that was removed from the game in patch 7.0), and a fallback that dropped to Cure I whenever Cure II would overheal. Now Cure I is only used before Cure II unlocks at 30 — and when a target is barely scratched (Cure II would overheal), the GCD goes to damage instead of a weak top-up; Regen, lilies and abilities cover small deficits

### Fix — Wrong action ids found by a full data audit (SAM, SGE, SMN)
- All 831 action ids were verified against the game sheets after the Dark Knight Unmend discovery. Three more jobs had wrong ids: **Samurai's Tengentsu** pointed at a Red Mage spell (the defensive never fired), **Sage's Psyche and Eukrasian Prognosis II ids were swapped** (Psyche — a damage cooldown — never fired), and **Summoner's Ifrit/Titan/Garuda summons** pointed at their level-90 upgrades (broken below 90). All corrected

### Fix — Ninja froze mid-Ten Chi Jin when Doton was already down
- The AoE Ten Chi Jin sequence's third cast can only be Doton, but the logic refused to recast Doton while one was already ticking — so the sequence hung until the buff expired (~5 seconds of nothing, every burst with a Doton down). Now: if Doton is already active, TCJ takes the single-target sequence instead; and once a sequence is started, it always completes

### Fix — Dark Knight's Unmend was a monster ability
- Unmend's action id pointed at "Boulder Clap" — a mob skill — so the game rejected every Unmend the plugin ever tried (wrong-job error): ranged pulls, add tagging, and out-of-range filler were all silently dead on Dark Knight. Corrected to the real Unmend

### New — Tanks: ranged filler during wall-to-wall transit + stance on duty pop
- All four tanks now fire their ranged GCD (Tomahawk / Lightning Shot / Unmend / Shield Lob) at the chasing pack while running between walls, instead of idling — the no-target pause during transit was a 12-14% uptime hole on wall-to-wall pulls. Only enemies already in combat with you are targeted, so nothing new ever gets pulled; toggle in the tank controls ("Transit Ranged Filler", default on)
- Tank stance (Iron Will / Defiance / Grit / Royal Guard) now enables as soon as you're in an instanced duty, instead of a few seconds into the first pull — no more early-pull enmity gaps
- Gap closers (Onslaught / Trajectory / Shadowstride / Intervene) are no longer used as travel tools: dashing at far targets mid-pull fought the movement plan. They now fire out-of-melee only to snap back a mob that's peeled onto someone else; in-melee damage weaves are unchanged (except Shadowstride, which deals no damage and is no longer weaved at all)

### Fix — Sage no longer casts Dyskrasia while out of range
- Dyskrasia only hits enemies within 5 yalms of the Sage, but the AoE decision was counting enemies around the *target* — so standing at casting range with a pack clustered ahead, Sage would cast Dyskrasia into empty air (zero damage) instead of Dosis. The count is now always centered on the player, so Dyskrasia fires only when enemies are actually in its radius

### Fix — Machinist no longer wastes the Automaton Queen on dying packs
- The Queen could deploy onto a mob at 4% HP moments before combat ended — she needs several seconds of Arm Punches before Pile Bunker, so that's pure Battery down the drain. She's now held on two signals: when even the healthiest enemy in range is nearly dead (default 5% HP), and when the pack's estimated time-to-kill from the recent damage rate is too short (default 8s) — trash packs don't get low, they melt, so a mob at 45% HP dying in 3 seconds is caught by the second check. Both thresholds are sliders in the Automaton Queen section; the Battery simply carries into the next pull

### New — Action log shows AoE target counts
- AoE actions in the debug action log now show how many enemies were inside the ability's radius when it fired (e.g. `[Scattergun ×3]`) — makes it easy to verify AoE-vs-single-target choices matched the pack size

### Fix — Machinist never used Full Metal Field
- Full Metal Field — the big 900-potency hit granted by Barrel Stabilizer — never fired. Its readiness check read the global GCD cooldown, which is always rolling mid-fight, so the check never passed: it locked out both the skill itself and the logic that holds Hypercharge until it's spent, and the proc quietly expired every burst. The burst now sequences properly (tools → Full Metal Field → Hypercharge + Wildfire), and Reassemble is no longer wasted on it (it already always crits)

### Fix — Casters can now weave abilities after hard casts
- Ability (oGCD) weaving only ever happened after instant spells — the slot math reserved the whole end of the GCD for the next cast, which ate the ~0.9s gap a hard cast leaves. When a caster had no instants available, cooldowns just piled up unused (Pictomancer muses, Striking/Starry Muse could sit ready for a whole fight). One ability now weaves into the post-cast gap, the same way experienced casters (and RSR) play it — burst tools come out noticeably earlier on every casting job

### Fix — Pictomancer Comet in Black never fired (and locked everything behind it)
- "Black paint" was read from a wrong gauge bit, so it always registered as missing and **Comet in Black never cast**. That one stuck flag cascaded: Monochrome Tones never got spent, so Holy in White (whose button morphs into Comet) was silently rejected every GCD, Subtractive Palette couldn't be pressed again, and with no instant spells left there were no weave windows — Striking Muse, Starry Muse and the muses all sat unused for entire fights. Black paint is now derived correctly (paint + Monochrome Tones, matching the game), Holy correctly steps aside while Comet is up, and the whole loop — Holy ↔ Comet, Subtractive re-press, hammer and muse weaves — flows again

### Fix — Pictomancer never used Subtractive spells or the Hammer combo
- Two whole Pictomancer subsystems were dormant. **Subtractive:** the gates were inverted — the free Subtractive Spectrum proc from Starry Muse *blocked* the Subtractive Palette press instead of triggering it, and a "save for Comet" hold waited on black paint that only exists *after* pressing — so Blizzard/Stone/Thunder in Cyan/Yellow/Magenta and Comet in Black never fired at all. Subtractive Palette now presses on the free proc and at 75+ gauge (pooled for burst), matching how the job is meant to play. **Hammer:** motif repainting was blocked whenever a cast was in progress — which for a caster is nearly always — so the weapon canvas never got painted mid-fight and Striking Muse + the Hammer Stamp/Brush/Polish combo never happened. Repainting now slots into the cast-queue window like every other spell. Also stops motifs being painted twice back-to-back during burst

### Fix — Samurai stopped mid-combo and spammed Jinpu after a target swap
- On the second mob of a pack (right after the previous target died), Samurai could get stuck casting Jinpu every GCD — never advancing to Gekko, so it built no Sen and lost Midare, Gekko, Kasha and all the burst that follows. The finisher step relied solely on the combo gauge, which can briefly desync after a target swap; the combo-continuation now also keys off the action just cast (the same resilience the combo opener already had), so Gekko/Kasha reliably follow Jinpu/Shifu and the combo never loops

### Fix — Action log now shows real casts, not queue submissions
- The debug action log (and GCD-uptime stat) recorded an entry every time the rotation *submitted* a GCD to the game's queue — but the game only casts the last one queued per GCD window, so fast rotations showed several actions crammed into one 2.5s window that never really happened. It now logs a GCD when it actually fires, so the timeline is one entry per real cast. Purely a logging/diagnostic fix — it doesn't change how any rotation plays

### Fix — Monk never reached Phantom Rush
- Solar Nadi was read from the wrong gauge bit, so it always registered as missing. Monk kept rebuilding Solar (Rising Phoenix) every Perfect Balance and never advanced to Lunar (Elixir Burst), so it never held both Nadi and **Phantom Rush** — its strongest GCD — never fired. Solar Nadi is now read correctly, restoring the full Rising Phoenix → Elixir Burst → Phantom Rush progression

### Fix — Monk Six-sided Star spam
- Six-sided Star was firing constantly (its gate wrongly assumed it consumes Chakra, which it doesn't), grabbing gaps in the form combo. It's now a movement-only filler — used only while moving and unable to continue the melee combo — so it no longer interrupts the normal rotation. Also cleaned up the raptor GCD selection to stop depending on a buff the game no longer applies

### Fix — Monk Perfect Balance / Masterful Blitz never fired
- Perfect Balance was being queued but never actually went off, so Monk never built Beast Chakra and never got a Masterful Blitz (Phantom Rush / Rising Phoenix / Elixir Burst) — a huge chunk of its damage. Two causes: the chakra spender was set to the highest weave priority and, with Chakra capped, ate every weave slot ahead of Perfect Balance; and PB's timing depended on a Disciplined Fist check that never reads active on current patch, so it could only ever fire inside Riddle of Fire. The spender now yields to the burst cooldowns, and PB is timed off Riddle of Fire like the rest of the burst — so it fires in the window and spends its spare charge between windows instead of overcapping

### Fix — Monk Riddle of Fire never fired
- Riddle of Fire was held indefinitely behind a "wait for Disciplined Fist" check that never released, so Monk's core damage buff (and its Fire's Reply follow-up) never went off. It now fires on cooldown like every other burst cooldown (RSR parity), with the burst/phase holds still respected

### New — Short command /dae

- Added `/dae` as a shorter alias for `/daedalus` — same window and subcommands (`toggle`, `debug`, `hardcast`). The full `/daedalus` still works

### Fix — Missing window no longer flags upgraded abilities

- The Missing window could list a base ability as "not unlocked" even when its trait upgrade was on your bar and being used (e.g. Monk's Howling Fist shown as missing while Enlightenment — its level-74 upgrade — was firing). It now recognizes that a castable upgrade means the ability is available, so only genuinely locked abilities (uncompleted job quests) are listed

### Fix — Viper combo could lock up

- Fixed a rare Viper stall where the rotation would freeze on its combo (Why Stuck showing the finisher rejected, nothing else firing): if the game's combo state briefly desynced, the finisher was rejected and nothing fell back to the basic combo. Viper now keeps the basic starter (Steel/Reaving Fangs, or Steel/Reaving Maw in AoE) ready as a fallback, so a rejected finisher restarts the combo instead of stalling — without disturbing positional holds

### Fix — Esuna now cleanses "esuna check" debuffs by default (all healers)
- Unrecognized dispellable debuffs were treated as low priority and skipped at the default Esuna setting. Many dungeon/trial/raid "cleanse or wipe" mechanics use a unique debuff the bot doesn't have hardcoded, so they could be missed. These now default to medium priority and get cleansed at the default threshold. Known harmless movement debuffs (Bind/Heavy/Blind) are unaffected. Applies to all four healers

### New — Auto-Manage BossMod AI by role (group content)
- New opt-in option (Nav Control → "Auto-Manage BMR AI by role") for group content, where AutoDuty's BMR management isn't running. When on and BossMod Reborn is loaded, Daedalus feeds BMR a role-based stand distance (healers/ranged hold at range — default 15y, melee hug) plus the **live next-GCD positional** (so RPR/MNK/NIN get flank↔rear correctly, not a single static positional), in movement-only mode so BMR handles the pathfinding/safety while Daedalus keeps the rotation and targeting. You still enable BMR AI yourself (`/bmrai`). Off by default; does nothing if BMR isn't loaded

### Improved — White Mage
- Lily cap prevention is now proactive: it spends a Lily at 2/3 when the next one is about to tick (not only at 3/3), so a Lily regen is never wasted — which also feeds Blood Lily → Afflatus Misery faster
- Co-healer GCD gating: with a co-healer present, a White Mage set to the **Co** role now leaves non-critical GCD heals to the Main healer and oGCDs to keep up DPS (parity with AST/SGE). Critical targets still get healed, and it has no effect when solo-healing. New "GCD Heals Only When Solo Healer" toggle under Co-Healer Coordination

### New — Debug Log tab + file
- Added a **Debug Log** tab to the Debug window that surfaces meaningful failures only — actions the game refused to cast (not facing / line of sight / etc.) and failed BossMod config pushes — without the per-frame rotation chatter. Repeated identical events are collapsed into one line with a running ×count and how long the condition has persisted (e.g. "×42 over 11.9s"), so a stall reads as one self-timing entry instead of a flood
- Refused-cast lines name the target and flag whether it matches your hard target ("hard target MISMATCH — auto-face turns elsewhere"), which pinpoints the common "stuck, won't cast" cause at a glance
- Also catches **no-dispatch stalls** — being stuck in combat with enemies engaged but nothing firing (no castable target in range), even when no cast was ever attempted. Travel between packs and intentional safety pauses (Pyretic / look-away) are excluded, so only real stalls log
- The same events are mirrored to `daedalus-debug.log` in the plugin config folder for after-the-fact inspection. Toggle file writing with "Write to file" in the tab (on by default); filter by category, copy, or clear from the same tab

### Fix — Casters no longer jam or stall while moving
- Fixed a rotation deadlock that could freeze a job for many seconds (seen on White Mage in AutoDuty): once a cast was interrupted in a certain way the rotation got stuck on "already submitted this GCD cycle" with the GCD sitting ready and nothing firing. It now recovers within a fraction of a second. This is a dispatcher-level fix, so it hardens every job
- While the character is moving — e.g. AutoDuty pathing you to the next mob or out of an AoE — casters and healers no longer keep trying to start hard-cast GCDs (Stone IV, Glare, Fire/Blizzard, etc.) that get interrupted before they finish, which previously left the rotation spinning. Hard-casts are now held while moving (this also tracks the plugin's own pathing, not just raw position) and instant GCDs — DoT refreshes, Swiftcast'd casts — carry the rotation until you stop

### Improved — Dancer dance partner
- Auto-partner now upgrades mid-fight: if a higher-priority partner becomes available (e.g. a better DPS revives or joins), Dancer switches Closed Position to them instead of only re-partnering when the current one dies. It only ever moves to a strictly better job, so it never flip-flops between equal partners (requires "auto re-partner")
- Refreshed the partner priority for the current patch — Pictomancer is now picked first, ahead of the melee, matching its top-tier value as a dance partner

### Fix — Dragoon combo broke after level 76
- Once Raiden Thrust (Lv.76) or Draconian Fury (Lv.82) replaced True Thrust / Doom Spike as the combo starter, the rotation no longer recognized the combo had started, so it kept re-pressing the starter instead of advancing to step 2 — stalling the whole 1-2-3. The combo now treats Raiden Thrust and Draconian Fury as valid starters, so the chain flows correctly at all levels

### Fix — Monk never reached Phantom Rush
- Perfect Balance was rebuilding the Nadi you already had instead of the one you were missing, so Monk would make Lunar (or Solar) over and over and never assemble both — meaning Phantom Rush, its strongest GCD (1500 potency), never fired. Perfect Balance now builds the missing Nadi each time (Solar = one of each form, Lunar = three Opo-opo GCDs), opening Solar first for the safest sequence, so the Lunar → Solar → Phantom Rush cycle works

### Fix — Viper Reawaken blocked in packs
- Reawaken required Noxious Gnash to be active for 10+ seconds on the current target. Because Noxious Gnash is a per-target debuff, swapping targets in a pack reset it to zero and silently blocked the entire Reawaken burst — overcapping Serpent's Offering. Reawaken now only checks that Hunter's Instinct and Swiftscaled (the buffs that must last through the burst) have enough duration, and Noxious Gnash is kept up separately by the Vicewinder path (same fix pattern as Reaper's Enshroud)

### Improved — Reaper ranged filler & smarter Enshroud
- Reaper now uses **Harpe** as a ranged filler when an AoE mechanic forces you out of melee range, so the GCD keeps rolling instead of dropping to auto-attacks (Harvest Moon is still preferred when Soulsow is up). Only used while standing at range — it won't waste a cast trying to fire while you're moving (unless Enhanced Harpe makes it instant)
- Enshroud no longer gets blown on a dying target in dungeons: if every enemy in range is about to die, it holds the burst. Never applies in trials/raids (boss HP makes it pointless). Tunable via "Skip Enshroud on dying target" under the Reaper Enshroud settings (default 5%, 0 to disable)

### New — Missing window (unlocked-ability check)
- Added a **Missing** window (button in the main window footer, next to Debug) that scans your current job's abilities and flags any that are high enough level but **not actually unlocked** — almost always an uncompleted job quest. Updates automatically for whatever job you're on. Handy when leveling via AutoDuty: if a key ability silently never fires (like Reaper's Enshroud before its Lv80 quest), this tells you exactly which quest you're missing. Expand "All expected abilities" to see the full unlocked/locked list

### New — Raid window (per-fight strategies)
- Added a **Raid** window (button on the main window) that shows the duty you're currently in and lets you set a per-fight targeting strategy for it. Turn on "Use a custom strategy for this fight" to override the enemy strategy, "switch off unreachable targets", strict explicit-target, and skip-invulnerable just for that duty — handy for split-boss fights where you want different targeting than your global default
- Overrides are saved per fight and applied automatically when you zone into that duty. Your global targeting settings are never changed, and a saved-strategies list lets you review or remove them

### New — Switch off unreachable targets (split bosses)
- When the enemy you're following is alive but out of reach — e.g. a boss split into an elevated "upper" part melee can't hit and a grounded "lower" part — Daedalus now switches to the reachable enemy and keeps doing damage instead of standing idle. It only fires when another enemy is actually in range, so chasing a single far-away target is never interrupted. New "Switch off unreachable targets" toggle under General → Targeting (on by default)

### Fix — No longer targets friendly NPCs
- Hardened targeting so Daedalus never locks onto friendly NPCs (Trust allies, escort/protect objectives, pets, chocobos) — only attackable hostiles can be auto-targeted or auto-faced

### Fix — Reaper Gluttony & Enshroud never firing
- Gluttony was effectively never used — its gate checked Enshroud's cooldown, but Enshroud is gauge-gated (its cooldown is almost always up), so the check was permanently false. Gluttony now fires on cooldown as the premium Soul spender (and at Lv.96 actually grants the Executioner stacks the previous fix added)
- Enshroud was being held in solo/AutoDuty: it required Arcane Circle active or Death's Design above 15s, so once the DoT ticked down with no party buff it never triggered. It now enters on cooldown once you have the Shroud gauge and Death's Design is up, which restores the whole Void/Cross Reaping → Communio burst
- Enshroud no longer waits on Death's Design and now fires as soon as you have 50 Shroud (outside burst pooling). Tying Enshroud to the DoT meant it never fired in packs when the current target briefly lacked Death's Design after a target swap
- Fixed slow Shroud generation: Death's Design was re-applying about twice as often as needed in packs (each target swap re-triggered it), stealing the Soul Reaver casts that build Shroud. It's now applied promptly when missing but no longer outranks your Shroud-building GCDs, so Shroud fills at full speed and Enshroud comes up on time

### Fix — Reaper Executioner finishers + Enshroud polish
- Fixed Executioner's Gibbet/Gallows/Guillotine (the Lv.96+ Gluttony upgrade) never being used — at 96+ Gluttony grants the Executioner buff, but the rotation only handled Soul Reaver, so the two high-potency stacks were wasted every Gluttony. They now fire with the correct flank/rear positionals, and a fresh Gluttony/Blood Stalk/Enshroud won't override pending Executioner stacks
- Ideal Host (free Enshroud) now triggers Enshroud even at low Shroud instead of being ignored
- Death's Design is now refreshed during a long Enshroud (so it doesn't drop mid-burst), Communio falls back to an instant Shadow of Death while moving (holding the last orb for Communio when you stop), and the basic combo is rushed to its finisher if the combo timer is about to lapse

### New — Tank wall-to-wall pulling
- While moving in a dungeon/trial, tanks now ranged-pull the nearest mob within 25y that isn't on them yet — including packs you're walking toward — so wall-to-wall pulls gather for AoE and nothing gets left behind. Works on all four tanks (PLD Shield Lob, WAR Tomahawk, DRK Unmend, GNB Lightning Shot)
- New "Tag Adds While Moving" toggle in the Control window (on by default). Only fires while moving, in an instanced duty, and never interrupts a combo

### New — Smarter tank add control
- "Don't Chase Lost Mobs": when a mob slips to another player, the tank no longer dashes after it — Provoke and a ranged attack reclaim it in place so you stay on the pack
- Paladin Holy Sheltron now spends Oath Gauge at cap for near-permanent physical mitigation uptime (toggle + threshold under the PLD Mitigation section)

### Fix — Paladin AoE
- Total Eclipse no longer loops forever without Prominence — the AoE combo now advances correctly
- AoE now triggers off enemies around you (the actual PBAoE radius), so spread packs no longer get treated as single-target
- A real pack breaks an in-progress single-target combo to AoE immediately instead of finishing the 1-2-3 first

### Fix — Samurai mid-rotation lockups
- Fixed stalls that dropped you into auto-attacks after Fuko/Gyofu and after weaving Kenki spenders
- Kaeshi: Namikiri and Tsubame-gaeshi follow-ups no longer get skipped after their Iaijutsu, and double Ogi Namikiri is prevented

### Fix — Rotation deadlock (all jobs)
- Fixed a stall where the rotation could sit idle for 10+ seconds doing nothing (e.g. Machinist after a combo GCD, or any job during AutoDuty). The internal "don't double-cast the same GCD" guard could get stuck and never release if nothing fired to reset it; it now clears as soon as the GCD recast finishes

### New — Auto-face target
- Daedalus now keeps the game's "Auto-face Target when using an action" setting on while it's running, so facing-required weaponskills no longer get refused while you're moving (e.g. AutoDuty running you around). Your original setting is restored when you disable or unload the plugin
- Look-away safety: while a gaze mechanic is being cast, auto-face is automatically suppressed so the bot's casts don't turn you into the boss (gaze action list is curated as mechanics are encountered)

### New — Auto-Peloton for ranged DPS
- Bard/Machinist/Dancer auto-cast Peloton while out of combat and moving (travel speed between pulls). Toggle under Shared Ranged Settings → Utility (on by default)

### Improved — Warrior
- Surging Tempest no longer drops during long Fell Cleave stretches — the rotation refreshes it (Storm's Eye) before it falls off
- Onslaught now weaves in burst (Inner Release) instead of on cooldown, and won't dash you while moving
- Bloodwhetting / Raw Intuition now reliably fires when you take damage: it has its own "Bloodwhetting HP Threshold" slider (default 70%) and, at or below that HP, weaves ahead of damage oGCDs so it's no longer starved out of the weave slot during burst (previously it could be skipped even down at ~17% HP). Set the slider to 100% to use it on cooldown as sustain
- Fixed Vengeance / Damnation never firing: at level 92+ the cooldown was queued but silently rejected by the dispatcher (it targeted the un-upgraded action id), so it sat unused all fight. Damnation now actually goes off
- Vengeance / Damnation now also fires on cooldown for big pulls: new "Vengeance Pull Size" slider (default 3) pops it when you're tanking that many or more engaged enemies (wall-to-wall), on top of the existing HP-based trigger
- New "Pre-pull Tomahawk" toggle (off by default): with an enemy targeted out of combat, opens the pull with Tomahawk

### Fix — Dark Knight
- The level-96 Delirium combo now completes: Comeuppance and Torcleaver fire after Scarlet Delirium instead of the combo stalling on the first hit — recovering the two biggest burst GCDs and the Disesteem proc. AoE Impalement under Delirium also fires reliably now
- Edge/Flood MP usage is smarter: Darkside refreshes before it lapses, MP dumps near cap outside burst, and during burst it spends down while keeping enough banked for The Blackest Night (closer to the 5/2 plan)
- The Blackest Night now also banks Dark Arts for damage: while you're actively tanking with MP to spare, TBN is used so the shield breaks and grants a free Edge/Flood (MP-neutral, plus a free shield). New "TBN Dark Arts banking" toggle (on by default); the HP-threshold slider still controls reactive shielding
- Dark Arts is now actually detected (it was being read as permanently off), so the free Edge/Flood from a broken TBN shield is recognized and the banking logic above works correctly
- Shadowstride no longer darts you around the pack: it's no longer woven as filler damage by default (only used to close the gap to an out-of-range target). New "Auto-weave Shadowstride" toggle (off) to opt back in
- Salted Earth (and its Salt and Darkness follow-up) now actually fire — they were blocked by a wrong action ID and never went off. Added a "Salted Earth Min Targets" slider (default 1 = on cooldown) so you can hold it for big wall-to-wall packs

### Fix — Pictomancer Hammer combo / Striking Muse never firing
- Fixed Striking Muse (and therefore the Hammer combo) never triggering, and Starry Muse firing late: their readiness was reading the cooldown off the wrong action (the morphed button instead of the base Steel/Scenic Muse gauge action). Now consistent with Living Muse, so the weapon/Hammer line and Starry burst come up on time
- Striking Muse and the Hammer combo now fire on cooldown in solo/Trust/dungeon content instead of being pooled to align with Starry Muse — short back-to-back pulls never reached that Starry window, so Hammer was being wasted. The Starry alignment still applies when coordinating burst with a party

### Fix — Pictomancer canvas/muse system dormant in pulls
- Fixed Pictomancer never using its motifs, muses, Hammer combo, portraits (Mog/Madeen), or Starry Muse during back-to-back pulls — it was stuck spamming only color spells. Motif painting was being out-prioritized by the basic combo and never fired in combat, so the canvases the whole system depends on were never created. Motifs are now painted in combat, timed to each muse's cooldown, so Living/Steel/Scenic Muse (and everything they enable) come online

### Fix — Pictomancer subtractive combo stall
- Fixed the subtractive AoE/single-target combo (Blizzard → Stone → Thunder) getting stuck after Stone, refusing to fire Thunder in Magenta and stalling the rotation. The combo-step detection was checking the wrong action for the final step, so it kept trying to recast the combo starter

### Fix — Stuck / spamming when not facing the target (all jobs)
- Fixed a case where the rotation would either stall or rapidly re-cast the same GCD (e.g. Pictomancer spamming Fire in Red during multi-mob pulls) when the auto-targeted enemy wasn't being faced. The game's auto-face only turns you on a *successful* cast, so a refused cast couldn't self-correct. Now, when a GCD is refused for facing, the target is re-faced (hard-targeted) so the next cast lands; submits that don't commit are throttled instead of re-firing every frame
- The "Why Stuck?" reason is now accurate for this case — it reads the real refusal (not facing / line of sight / out of range) instead of guessing

### Fix — Gunbreaker
- Bloodfest now actually fires: it was being cast on yourself, but the game requires it on an enemy target, so it sat "ready" and never went off (which also meant the Lv100 Reign of Beasts → Noble Blood → Lion Heart combo never triggered). Bloodfest now lands once per cooldown and the Reign combo fires inside No Mercy
- Royal Guard (tank stance) now auto-enables in combat like the other tanks — Gunbreaker previously never turned its stance on
- Fixed a mid-pull lockup where the rotation could freeze for ~10+ seconds after Bloodfest: the basic combo could desync and stop dispatching, starving cartridges and stalling No Mercy. The combo now always falls back to its starter and self-corrects

### New — Gunbreaker proactive mitigation
- Sustain cooldowns (Camouflage, Rampart, Nebula) now fire proactively on wall-to-wall pulls instead of waiting for your HP to drop — so you're mitigated before the damage lands, not after. New "Proactive Mit Pull Size" slider (default 3) under the GNB Mitigation section

### New — Main / co-healer roles
- New "My Healer Role" setting under Shared Healer Settings → Co-Healer Coordination (Auto / Main / Co). In a two-healer party, set one healer to Main (owns GCD heals) and the other to Co (defers non-critical GCD heals to the Main, sticking to oGCDs, shields, and DPS). This fixes the case where two auto-detecting healers would both defer to each other and neither would proactively GCD-heal. Applies to Astrologian and Sage; solo healing is unaffected

### Fix — Astrologian
- Divination now actually fires in solo, Trust, and AutoDuty content. It was being held for a party burst-alignment signal that only exists with multibox IPC coordination, so in uncoordinated content it sat unused the entire fight (a large DPS loss). It now falls back to using it on cooldown (~8s into combat) when no burst coordination is present, and still aligns to the party window when coordinating

### Improved — Astrologian
- Essential Dignity now uses per-charge thresholds instead of sitting on charges: a spare charge is spent proactively (new "spare charge" threshold, default 70%) while the final charge is banked for emergencies ("last charge" threshold, default 60%) — more healing throughput without losing the safety net
- New "GCD Heals Only When Solo Healer" toggle (on by default): with a co-healer in the party, non-critical Benefic/Helios casts are left to oGCDs and the co-healer to keep damage uptime. Critical targets still get a GCD heal, and solo healing is unaffected

### Improved — "Why Stuck" diagnostics (all jobs)
- Live "Last action: Ns ago" idle timer, a PAUSED banner that names why the whole rotation is idle (including "no action in combat"), and per-ability reasons for why a GCD won't fire (cooldown, proc, combo, out of range, line-of-sight/facing). The tank tab also shows enemy counts (in PBAoE range vs aggroed within 25y)
- Added a vNav movement state (Idle / Pathing / Finding path) and a live "In LoS / facing" enemy counter, so it's clear whether an idle is the character moving vs. no enemy actually being castable-at (line of sight / facing)
- The "why a GCD won't fire" reason is now accurate for internal holds too (repeat-GCD guard, submit latch, submit backoff) instead of mislabeling them as line-of-sight/facing

## v0.0.3 — 2026-06-26

### Fix — Melee and ranged accuracy
- Ninja, Machinist, and Monk: filled burst-window gaps, fixed GCD dispatch and Monk form handling, and tightened always-be-casting so there are fewer idle gaps
- Improved AoE target selection and combat detection across jobs (RSR parity)

## v0.0.2 — 2026-06-24

### Fix — Healers
- Sage and the other healers: better Phlegma pacing, DoT uptime, AoE healing thresholds, and overall heal stability

### New — Navigation and movement
- Added the global Nav Control window (vNav flex deadband, solo position lock, debug rings) and melee auto-movement with solo gating

## v0.0.1 — 2026-06-24

### New — Initial Daedalus build
- Renamed from Olympus to Daedalus (v0.1.0 line) and brought the first wave of job rotations online: tanks (PLD/WAR/GNB), healers (SGE/AST/SCH/WHM scope), and melee/caster DPS with proc-gate parity, combo fallbacks, and BossMod/vNav integration
