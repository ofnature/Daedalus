# Occult Crescent — encounter checklist vs the weakness table

> Built 2026-08-10. Canonical lists come from **game data** (the `DynamicEvent` and `Fate`
> sheets via XIVAPI), which is authoritative for *what exists*; boss names come from
> consolegameswiki, which is authoritative for *which enemy in an encounter is the boss*.
> Coverage columns are from `Daedalus/Data/OccultWeaknessSeed.json` at 267 rows.

## Why both sources are needed

The seed alone cannot tell you what you are missing — it only knows what has been seen. And
the "biggest HP in the encounter is the boss" heuristic is **wrong** for several FATEs, where
the adds out-HP the boss:

| FATE | Biggest HP member | Actual boss | Boss HP |
|---|---|---|---|
| Sworn to Soil | Common Compost 2,108,960 | **Mad Mudarch** | 1,891,473 |
| The Winged Terror | Petrifog 2,579,710 | **Giant Bird** | 2,165,450 |
| Eye to Eye (N) | Accursed Orb 423,675 | **Evil-seer** | 423,675 |

All three of those bosses are already scanned. Judging by HP alone reported them as gaps.

## Counts

| Zone | Critical Encounters | FATEs | Sheet rows |
|---|---|---|---|
| South Horn | **15** | **13** | `DynamicEvent` 33–47, `Fate` 1962–1977 |
| North Horn | **15** | **13** | `DynamicEvent` 49–63, `Fate` 2072–2084 |

`DynamicEvent` 48 and 64/65 are The Forked Tower (Blood / Magic) — 48-player raids, not CEs.
Row 32 (The Dalriada) is the last Bozja/Zadnor entry: it caps at 48 participants where every
Occult encounter caps at 72, which is how the zone boundary was confirmed.

## South Horn — Critical Encounters (15)

| CE | Boss | In table? | Weakness |
|---|---|---|---|
| Scourge of the Mind | Mysterious Mindflayer | **NEVER SEEN** | wiki: Fire |
| The Black Regiment | Black Star | yes | Lightning ✓ |
| The Unbridled | Crescent Berserker | yes | Fire ✓ |
| Crawling Death | Death Claw | yes | Ice ✓ |
| Calamity Bound | Cloister Demon | yes | Ice ✓ |
| Trial by Claw | Crystal Dragon | yes | Wind ✓ |
| From Times Bygone | Mythic Idol | **NEVER SEEN** | wiki: Lightning |
| Company of Stone | Megaloknight | yes | Lightning ✓ |
| Shark Attack | Nymian Petalodus | **NEVER SEEN** | wiki: Lightning |
| On the Hunt | Lion Rampant | yes | Lightning ✓ |
| With Extreme Prejudice | Command Urn | yes | Lightning ✓ |
| Noise Complaint | Neo Garula | yes | Fire ✓ |
| Cursed Concern | Trade Tortoise | **NEVER SEEN** | wiki: Ice |
| Eternal Watch | Repaired Lion | yes | Lightning ✓ |
| Flame of Dusk | Hinkypunk | yes | Wind ✓ |

Soul-shard CEs: On the Hunt → Oracle, The Black Regiment → Ranger, The Unbridled → Berserker.

## South Horn — FATEs (13) — all recorded

Bosses scanned **12 / 13**. The only gap is the one already known:

| FATE | Boss | Weakness |
|---|---|---|
| Rough Waters | **Nammu** | **NOT SCANNED** (wiki: Lightning) |
| A Delicate Balance | Dehumidifier | Ice ✓ |
| A Prying Eye | Observer | Wind ✓ |
| An Unending Duty | Sisyphus | Fire ✓ |
| Brain Drain | Advanced Aevis | Ice ✓ |
| Fatal Allure | Execrator | Wind ✓ |
| King of the Crescent | Ropross | Fire ✓ |
| Persistent Pots | Crescent Garula | Fire ✓ |
| Pleading Pots | Havoc | Lightning ✓ |
| Serving Darkness | Lifereaper | Fire ✓ |
| Sworn to Soil | Mad Mudarch | Ice ✓ |
| The Golden Guardian | Gilded Headstone | Lightning ✓ |
| The Winged Terror | Giant Bird | Wind ✓ |

## North Horn — Critical Encounters (15)

Bosses scanned **13 / 14 recorded**:

| CE | Boss | In table? | Weakness |
|---|---|---|---|
| Many Mouths to Feed | Pelekys | **NEVER SEEN** | unknown |
| Ahead of the Competition | Phantom Hydra | yes | **NOT SCANNED** (wiki: Ice) |
| A Beast Unleashed | Atlas Carbuncle | yes | Ice ✓ |
| Accept No Imitators | Metamorph | yes | Wind ✓ |
| Appalling Behavior | Pallmagia | yes | Fire ✓ |
| Cursed Resurgence | Claret Dragon | yes | Fire ✓ |
| Dark Artistry | Phantom Necromancer | yes | Wind ✓ |
| Doubled Trouble | Conjured Calofisteri | yes | Wind ✓ |
| Familiar Tactics | Elm Gigas | yes | Lightning ✓ |
| Forbidden Folios | Arbatel | yes | Fire ✓ |
| Imbalanced Diet | Algol | yes | Fire ✓ |
| Lost on the Wind | Abductor | yes | Lightning ✓ |
| Quarried Away | Alabaster Blade | yes | Lightning ✓ |
| Tiny Terror | Tiny Mage | yes | Lightning ✓ |
| Web of Terror | Crescent Arachne | yes | Ice ✓ |

Soul-shard CEs: Dark Artistry → Necromancer, Appalling Behavior → Blue Mage. The other six
North Horn phantom jobs are bought from the Expedition Antiquarian, not dropped.

## North Horn — FATEs (13) — all recorded, all bosses scanned

A Rotten Affair (Patient Kuribu) · Allure of the Occult (Sensual Sandy) · Daylight Pottery
(Crimson Gremlin) · Eye to Eye (Evil-seer) · Gale-force Encounter (Stormcaller) · In a Pot of
Bother (Greater Fan) · Inconstant Gardener (Iambe) · Raging Thrall (Machetaur) · Scale Model
(Demi-Medusa) · Shoreline Showdown (Regnant Chimera) · Territorial Dispute (Ruin Hound) ·
Thunderregnum (Cresceregina) · Waved Away (Arch Kelpie).

## The to-do list

1. **Nammu** — Rough Waters, South Horn (24.6, 34.8). The one South Horn FATE boss unscanned.
2. **Phantom Hydra** — Ahead of the Competition, North Horn (19.8, 31.1). The only recorded CE
   boss in either zone without an element.
3. **Five encounters never seen at all** — South Horn: Scourge of the Mind (27.4, 35.9),
   From Times Bygone (5.5, 26.4), Shark Attack (19.1, 4.5), Cursed Concern (22.9, 10.5).
   North Horn: Many Mouths to Feed (4.1, 10.3), player-spawned by killing Crescent Wamouras.
4. **North Horn trash** — 21 of 23 recorded field mobs still have no element; that is now the
   single largest gap in the table (South Horn trash is 36/37 done).

## Validation note

Every boss weakness the table has recorded independently **matches** the community data —
roughly twenty encounters across both zones, zero disagreements. The observational pipeline is
producing correct data; the only issue is coverage, not accuracy. Do NOT backfill the table
from a wiki: `ElementalWeaknessLog` is deliberately observational ("records what the game
showed, never guesses"), and injecting unverified values would destroy that guarantee. The
wiki weaknesses above are listed as *predictions to confirm*, not as data.

## Possible follow-up

The canonical lists are stable game data, so the plugin could carry them and report "you have
never seen Shark Attack" directly in the Duty tab instead of needing this cross-reference done
by hand. Not built — the sheet ids above are what it would need.
