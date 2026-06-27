# Changelog

All notable changes to Daedalus will be documented in this file.

<!-- LATEST-START -->
## v0.1.0 — 2026-06-27

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

### New — Auto-face target
- Daedalus now keeps the game's "Auto-face Target when using an action" setting on while it's running, so facing-required weaponskills no longer get refused while you're moving (e.g. AutoDuty running you around). Your original setting is restored when you disable or unload the plugin
- Look-away safety: while a gaze mechanic is being cast, auto-face is automatically suppressed so the bot's casts don't turn you into the boss (gaze action list is curated as mechanics are encountered)

### New — Auto-Peloton for ranged DPS
- Bard/Machinist/Dancer auto-cast Peloton while out of combat and moving (travel speed between pulls). Toggle under Shared Ranged Settings → Utility (on by default)

### Improved — Warrior
- Surging Tempest no longer drops during long Fell Cleave stretches — the rotation refreshes it (Storm's Eye) before it falls off
- Onslaught now weaves in burst (Inner Release) instead of on cooldown, and won't dash you while moving
- New "Pre-pull Tomahawk" toggle (off by default): with an enemy targeted out of combat, opens the pull with Tomahawk

### Improved — "Why Stuck" diagnostics (all jobs)
- Live "Last action: Ns ago" idle timer, a PAUSED banner that names why the whole rotation is idle (including "no action in combat"), and per-ability reasons for why a GCD won't fire (cooldown, proc, combo, out of range, line-of-sight/facing). The tank tab also shows enemy counts (in PBAoE range vs aggroed within 25y)
- Added a vNav movement state (Idle / Pathing / Finding path) and a live "In LoS / facing" enemy counter, so it's clear whether an idle is the character moving vs. no enemy actually being castable-at (line of sight / facing)
- The "why a GCD won't fire" reason is now accurate for internal holds too (repeat-GCD guard, submit latch, submit backoff) instead of mislabeling them as line-of-sight/facing
<!-- LATEST-END -->

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
