# Daedalus

![Version](https://img.shields.io/github/v/release/ofnature/Daedalus?label=version)
![Downloads](https://img.shields.io/github/downloads/ofnature/Daedalus/total)
![Code Size](https://img.shields.io/github/languages/code-size/ofnature/Daedalus)
![Last Commit](https://img.shields.io/github/last-commit/ofnature/Daedalus)
![C#](https://img.shields.io/github/languages/top/ofnature/Daedalus)

An intelligent rotation assistant for FFXIV that goes beyond automation. Daedalus provides **intelligent decision-making** through fight prediction, cross-machine party coordination, a built-in DPS parser, performance analytics, and an integrated training system — built for multibox play and solo players alike.

## Installation

1. Open the Dalamud Plugin Installer in-game
2. Go to **Settings** (gear icon) → **Experimental**
3. Under "Custom Plugin Repositories", add:
   ```
   https://raw.githubusercontent.com/ofnature/Daedalus/main/repo.json
   ```
4. Click **Save and Close**
5. Search for **Daedalus** and install

Updates are delivered automatically — the plugin also checks for new versions itself (Settings → check for updates).

This repository URL also serves two companion plugins through the same installer — no extra URLs needed:

| Plugin | What it does |
|--------|--------------|
| **[Charon](https://github.com/ofnature/Charon)** | Auto pillion with smart seat scanning and whitelisted auto group invite — companion to Daedalus's LAN party coordination |
| **[SealBreaker](https://github.com/ofnature/SealBreaker)** | Automates dungeon farming, GC expert delivery, and grand company item purchasing (AutoDuty/ADS support) |

<details>
<summary>Manual installation</summary>

1. Download `latest.zip` from [Releases](https://github.com/ofnature/Daedalus/releases/latest)
2. Extract to `%APPDATA%\XIVLauncher\installedPlugins\Daedalus\`
3. Reload plugins or restart the game
</details>

## Highlights

| Feature | Description |
|---------|-------------|
| **Built-in DPS Parser** | ACT-style meter tracking everyone — your toons, other players, and Trust/duty-support allies — with DoT tick attribution and exact cross-toon numbers over the LAN hook |
| **LAN Coordination** *(WIP)* | Daedalus instances on **different PCs** coordinate over your local network — no router setup, no external tools |
| **Blue Mage Support** | Full BLU support with role selection (Tank/DPS/Healer), automatic Aetheric Mimicry, loadout checklists, and a spell farm planner |
| **Fight Awareness** | Bundled savage/ultimate timelines plus live BossMod integration predict raidwides and tankbusters before they happen |
| **Healer & Tank Coordination** | Multi-toon heal triage, mitigation stacking, tank swaps, and raise assignment — over same-machine IPC *and* cross-machine LAN |
| **Training Mode** | Learn *why* abilities are chosen with real-time explanations and skill tracking |

## Built-in DPS Parser

A damage meter that needs no external tools:

- **Tracks everyone in the fight** — your toons, other players (tagged `HUMAN`), and Trust/duty-support NPCs (tagged `TRUST`), with pet and summon damage merged into their owner's row
- **DoT ticks count** — tick damage is attributed to whoever applied the effect, so Chaos Thrust, Dia, and Higanbana show up in the numbers (most hook-based meters silently drop them)
- **Exact cross-toon numbers via the LAN hook** — every Daedalus toon broadcasts its own parse (always exact for itself); partied toons merge those authoritative reports, marked with a green dot
- **Borderless overlay mode** — compact semi-transparent bars with optional click-through and hide-out-of-combat, plus a name-scramble toggle for streaming
- Fight history dropdown, per-row crit/direct-hit tooltips, and damage-share bars with your own toon highlighted in gold

## LAN Coordination *(work in progress)*

Multibox across **multiple PCs** on the same network — UDP broadcast on your local VLAN, unified with same-machine IPC through one coordination bus:

- **Party Coordination window** — every toon across all machines with online status, job, HP, role slot, heartbeat health, and latency, plus a live alert feed (all-healers-down, tank swap, add spawn, raise incoming), a DEAD/raise marker, and a target-agreement line
- **Party target modes** — steer the whole party's targeting from the window: **Focus** (everyone burns one enemy you pick and ignores adds), **Split** (DPS spread across the pack, balanced by time-to-kill so the pack dies together), and **Kill Adds** (DPS + a designated off-tank peel to adds). The Main Tank is never pulled off the boss — protection is the default, and you nominate an off-tank explicitly
- **Role negotiation on zone-in** — tanks/healers/DPS slotted identically on every machine, automatically
- **Coordinated burst** — all toons open their burst windows simultaneously on signal, with per-toon burst-ready pips and a Force Burst button to align one on demand
- **Healer-down detection** — broadcasts when every healer is dead so a designated toon can respond
- **DPS report sharing** — feeds the parser exact per-toon numbers (see above)
- Name scrambling for stream-safe screenshots

Two-machine live testing is ongoing — expect rough edges.

## Farm Mode *(work in progress)*

Grind a material from specific overworld mobs, hands-free, until a target amount is in your bag. Daedalus pulls like a player would: walk to ranged tag distance, tag the mob, kill it while it runs at you, finish in melee, clear anything that aggroed you, then take the next — roaming between your farm spots while waiting on respawns. Single zone only (no teleporting). Requires **vnavmesh** for movement.

**How to start:**
1. Settings → General → Farm → enable **Show Farm button on main window**
2. Click **Farm** on the main window
3. Search the item by name and set your target count (current bag count shows live)
4. Click **Find droppers** — every mob that drops the item (via GarlandTools, the same data Monster Loot Hunter shows) is listed with level and zone. Clicking one adds it to the kill list, flags its spawn on your map, and — if it lives in your current zone — adds a farm spot automatically. Or do it manually: target a mob → **Add current target**, stand where they spawn → **Add spot (my position)**
5. **Start farming** — progress reports in chat, a green dot marks the active run, and it stops on its own when the target count is in your bag

The mob/spot list is session-only for now (not saved on logout); saved farm profiles and further polish are on the roadmap — design notes live in `docs/farm-mode.md`.

## Supported Jobs (22/22)

| Role | Jobs | Status |
|------|------|--------|
| **Healers** | White Mage, Scholar, Astrologian, Sage | ✅ Complete |
| **Tanks** | Paladin, Warrior, Dark Knight, Gunbreaker | ✅ Complete |
| **Melee DPS** | Monk, Dragoon, Ninja, Samurai, Reaper, Viper | ✅ Complete |
| **Ranged Physical** | Bard, Machinist, Dancer | ✅ Complete |
| **Casters** | Black Mage, Summoner, Red Mage, Pictomancer | ✅ Complete |
| **Limited** | Blue Mage | ✅ v1 (Moon Flute planner WIP) |

### Blue Mage

BLU has no fixed role, so Daedalus makes it explicit:

- **Role dropdown** (DPS / Tank / Healer) drives the rotation *and* automatic **Aetheric Mimicry** — the party and nearby players are scanned for the right archetype, reapplied after death or role change
- Tank role maintains **Mighty Guard** and fires **Diamondback**; Healer role handles **White Wind** thresholds
- **Loadout awareness** — the rotation only uses spells that are learned *and slotted* in your active set
- **Role loadout checklists** in the Missing window (Blue Academy reference sets): ✔ slotted / ● learned-not-slotted / ✗ not learned with its farm source — the window doubles as a spell-hunting planner for all 124 spells

## Core Features

### Intelligent Rotation
- **Level-sync awareness** — abilities adjust to your current level, including low-level dungeon syncs
- **Resource management** — Lily, Aetherflow, Kenki, Heat, Soul Voice, and every job gauge
- **oGCD weaving** — optimal ability timing without clipping
- **Positional indicator** — real-time rear/flank/front display for melee, suppressed when True North is active or the target is immune
- **Smart AoE targeting** — directional AoEs automatically target the enemy that hits the most targets
- **Proc tracking** — never waste a proc or let buffs fall off
- **Trust/Duty Support aware** — party logic (dance partners, heal targeting, co-tank checks) works with NPC allies, not just players

### Fight Timeline Integration
- **Raidwide prediction** — pre-shield and pre-heal before damage hits
- **Tankbuster awareness** — mitigations timed for incoming hits
- **Bundled timelines** — Pandaemonium and Arcadion savage tiers plus all six ultimates
- **BossMod (BMR) integration** — live raidwide/tankbuster countdowns in the overlay for *any* duty BossMod knows, plus position/dash safety checks for movement

### Party Coordination — IPC and LAN
When multiple toons run Daedalus (same machine or across the network):

| Coordination Type | What It Does |
|-------------------|--------------|
| **Heal Coordination** | Prevents double-healing the same target |
| **AOE Heal Sync** | Staggers party heals to avoid overlap |
| **Mitigation Stacking** | Prevents wasting Divine Veil + Temperance together |
| **Raise Coordination** | Only one healer raises each dead player |
| **Burst Windows** | DPS align raid buffs for maximum damage |
| **Tank Swaps** | Coordinated Provoke/Shirk sequences |
| **Interrupt Priority** | One player per interruptible cast |
| **DPS Reports** | Exact per-toon parse numbers shared to every meter |

### Visual Overlay (Draw Helper)
- **Attack range rings** — melee and ranged ranges around your character
- **Enemy hitboxes** — targeted enemy hitbox for precise distance judgement
- **Positional zones** — rear/flank/front zones drawn on your target

### Performance Analytics
- **Real-time metrics** — GCD uptime, deaths, near-deaths during combat
- **Post-fight scoring** — letter grades (S/A/B/C/D) with breakdown
- **Downtime analysis** — categorizes lost GCDs (movement, death, mechanics)
- **Session history** and **FFLogs integration**

### Training Mode
- **Live explanations** — see *why* each ability is chosen in real-time
- **Decision validation** — optimal (✓), acceptable (≈), or suboptimal (✗)
- **525+ concepts, 147 lessons, 735 quiz questions** across all jobs
- **Spaced repetition** with skill-level detection and adaptive detail

## Quick Start

1. `/daedalus` — open the main window
2. Click **Enable** to activate
3. Enter combat on any supported job
4. **Parser** for the damage meter, **Analytics** for performance, **Training** to learn as you play

## Commands

| Command | Description |
|---------|-------------|
| `/daedalus` | Open main window |
| `/daedalus toggle` | Enable/disable rotation |
| `/daedalus debug` | Open debug window |

## Job Modules

Each rotation is named after a Greek deity:

| Role | Job | Module | Role | Job | Module |
|------|-----|--------|------|-----|--------|
| Healer | White Mage | Apollo | Melee | Viper | Echidna |
| Healer | Scholar | Athena | Ranged | Bard | Calliope |
| Healer | Astrologian | Astraea | Ranged | Machinist | Prometheus |
| Healer | Sage | Asclepius | Ranged | Dancer | Terpsichore |
| Tank | Paladin | Themis | Caster | Black Mage | Hecate |
| Tank | Warrior | Ares | Caster | Summoner | Persephone |
| Tank | Dark Knight | Nyx | Caster | Red Mage | Circe |
| Tank | Gunbreaker | Hephaestus | Caster | Pictomancer | Iris |
| Melee | Monk | Kratos | Limited | Blue Mage | Proteus |
| Melee | Dragoon | Zeus | | | |
| Melee | Ninja | Hermes | | | |
| Melee | Samurai | Nike | | | |
| Melee | Reaper | Thanatos | | | |

## Roadmap

| Milestone | Status |
|-----------|--------|
| All 21 combat jobs | ✅ Complete |
| Fight timeline integration | ✅ Complete |
| Party coordination via IPC | ✅ Complete |
| Performance analytics + FFLogs | ✅ Complete |
| Training mode + coaching | ✅ Complete |
| Built-in DPS parser (+ LAN reports, DoT attribution) | ✅ Complete |
| Blue Mage v1 (role kit, mimicry, loadouts) | ✅ Complete |
| Cross-machine LAN coordination | 🚧 In progress |
| BLU Moon Flute burst planner + multi-BLU coordination | 📋 Planned |
| Countdown/pre-pull burst alignment IPC | 📋 Planned |

## Contributing

Issues and pull requests welcome at [GitHub](https://github.com/ofnature/Daedalus).

## License

This project is provided as-is for personal use with FFXIV.
