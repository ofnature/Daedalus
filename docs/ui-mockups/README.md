# Daedalus UI Overhaul — Mockups & Design Decisions

Approved 2026-07-03. These HTML files render the target look for every plugin window —
open them in any browser. The design system source of truth is `.cursor/rules/SKILL.md`
(palette Vec4s, layout rules, anti-patterns). The palette here maps 1:1 to those Vec4s:

| Name | Vec4 (SKILL.md) | Hex |
|------|------------------|-----|
| BgDeep | 0.08, 0.08, 0.10 | `#141419` |
| BgPanel | 0.12, 0.12, 0.15 | `#1F1F26` |
| BgRow (60%) | 0.15, 0.15, 0.18 | `rgba(38,38,46,.6)` |
| AccentGold | 0.85, 0.65, 0.20 | `#D9A633` |
| AccentDim | 0.55, 0.42, 0.13 | `#8C6B21` |
| StatusGreen | 0.20, 0.75, 0.35 | `#33BF59` |
| StatusYellow | 0.85, 0.75, 0.10 | `#D9BF1A` |
| StatusRed | 0.85, 0.25, 0.20 | `#D94033` |
| StatusGrey | 0.45, 0.45, 0.50 | `#737380` |
| TextPrimary | 0.92, 0.90, 0.85 | `#EBE6D9` |
| TextSecondary | 0.60, 0.58, 0.55 | `#99948C` |
| TextDisabled | 0.35, 0.35, 0.38 | `#595961` |

Shared implementation constants live in `Daedalus/Windows/Common/DaedalusTheme.cs`.

## Approved decisions per window

### main-and-overlay.html
**Main window**: status row = enabled dot + active preset + live GCD uptime%; rotation
line = codename in gold + `(JOB Lv.X)` secondary; duty/boss context line below in
disabled grey. The Enable/Disable button is THE one gold-filled control in the whole
plugin (restraint rule). Window-launcher grid stays 2-col; LAN Party button gets a
small green presence dot while the coordinator is connected. Footer: small-button
Changelog · Debug · Missing + version right-aligned.

**Overlay**: semi-transparent (~0.88 bg alpha), no title bar. Status pill toggle
(click = pause), "Next:" action in gold (loudest element), one-line HP/Party/GCD strip,
then BossMod timeline rows: severity-tag (RAIDWIDE yellow / TANKBUSTER red / DOWNTIME
grey), countdown turns yellow inside ~8s, rows beyond the first dim.

### party-coordination.html
Role icon per toon (shield=tank, cross=healer, swords=DPS) doubles as sync health:
hollow grey outline = no data yet; filled green = synced (heartbeat < 5s); filled
yellow = sync issue (heartbeat 5-30s stale OR role-slot mismatch); filled red =
connection lost (> 30s / dropped). Right-hand column carries the diagnosis text
("hb 3.2s late", "lost 12s ago"). Legend lives in a (?) hover tooltip bottom-right,
NOT a visible row. Machine headers: local gold, remote dim-gold; stale peer HP bars
render grey. ImGui impl: glyph in TextDisabled = hollow; DrawList rounded rect in
status color behind dark glyph = filled. Game role icons via ITextureProvider are the
stretch goal.

### config-window.html
Sidebar: search box, group headers in dim grey small-caps, active item = gold text +
2px gold left accent bar + 10% gold wash (replaces the old Olympus blue selection).
Section content header: job name in gold + codename in disabled grey + validation
status chip right-aligned (green validated / yellow pending / grey untested — from a
static per-job ledger). Gold SeparatorText groups. Shared settings shown in job
sections get a "— shared ranged setting" annotation. Footer (Reset section / Apply
preset) is mocked but NOT approved for implementation yet — new behavior, decide later.

### missing-blu-loadouts-and-nav.html
BLU loadout checklist (future, with Proteus UI work): collapsible Tank/DPS/Healer
headers with "N/24 slotted · M not learned" summaries; per spell ✔ green
learned+slotted / ● yellow learned-not-slotted / ✗ red not-learned with farm source.
Nav Control: gold SeparatorText sections, unchanged structure.

## Implementation checklist
- [x] DaedalusTheme.cs shared palette + helpers (GoldHeader, HelpMarker, StatusDot)
- [x] MainWindow restyle + uptime% + duty line + gold master button + LAN dot
- [x] OverlayWindow restyle + severity tags + urgency color
- [x] LanPartyWindow role/sync icons + diagnosis column + (?) legend tooltip
- [x] ConfigSidebar gold selection + group headers
- [x] Config section header convention (name + codename + validation chip)
- [ ] BLU loadout checklist (blocked on Proteus v2 slotted-set reads)
- [ ] Sweep remaining windows (Debug, Analytics, Control, Action Feed) onto DaedalusTheme
