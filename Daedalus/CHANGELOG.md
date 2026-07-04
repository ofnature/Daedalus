# Changelog

All notable changes to Daedalus will be documented in this file.

<!-- LATEST-START -->
## v0.1.3 — 2026-07-04

### New — Automation bridges, round two: AutoDuty + auto-engage
- **AutoDuty** runs now auto-start the rotation — no more manually enabling Daedalus before a farming session. This also covers Henchman's duty hunt-log marks ("Solo Unsync Duty" / duty-support dungeons): Henchman hands those dungeons to AutoDuty, so Daedalus follows AutoDuty's running state and fights inside the instance
- **Automation now opens on passive marks**: hunt-log and quest mobs usually won't attack first, and Daedalus previously waited for combat to start — so Henchman would target a mark and both plugins would stare at it forever. While automation is driving, a live attackable hard target now counts as "engage": Daedalus fires the opener the moment the driver targets the mob (and waits politely while you're mounted). Manual play is unchanged — Daedalus still never pulls on its own
- The main window and overlay now show **who's driving** in gold next to the duty label — "Dungeon · Henchman" (or AutoDuty / Quest) — and the Debug window's Why Stuck tab gained an "Automation" line (override held? engaged? waiting on what?) plus a build stamp in the header

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

### Fix — the v0.1.2 bridges could switch Daedalus on but not make it act
- Rotations read an internal snapshot of the settings (the duty-tuning overlay), and the automation on-switch never reached that snapshot — so Questionable/Henchman would report the rotation "started" while every module still saw itself as off. The switch is now visible to the rotation the same frame it flips. Validated end-to-end on overworld hunt-log farming: marks get targeted, opened on, and killed, and the task advances on its own
<!-- LATEST-END -->

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
