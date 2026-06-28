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
