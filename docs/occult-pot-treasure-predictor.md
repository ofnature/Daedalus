# Occult Crescent — pot treasure predictor

Scope for predicting Occult coffers: where they can be, what tier they'll be, and where the pot
FATE's hidden coffer ends up. Written 2026-08-01 from a read of BOCCHI (`.cursor/bocchi`) plus
what Daedalus already ships.

## Two different problems under one name

"Pot treasure predictor" can mean either of these, and they need different machinery:

- **A — tier prediction.** Before you travel, know whether this pot FATE will pay a bronze or a
  gold coffer. Cheap: we already have the evidence and the plumbing.
- **B — location prediction.** During the "Cache Me if You Can" hunt, know where the hidden
  coffer is instead of sweeping the area. Much harder; the mechanic is not understood yet.

Both are covered below. **A is worth doing now; B starts as data collection, not prediction.**

## The big technique: the zone layout knows every chest spot

BOCCHI's `TreasureHunt.CreatePathfinder()` does something we don't: it reads the **layout**, not
the object table.

```
LayoutWorld.Instance()->ActiveLayout
  -> InstancesByType[InstanceType.Treasure]
     -> per instance: GetTransformImpl()->Translation   (world position)
     -> per instance: uint at offset +0x30              (Treasure sheet row id)
        -> Treasure sheet -> SGB.RowId                  (1596 bronze / 1597 silver)
```

That enumerates **every treasure spawn point in the zone with its tier, whether or not a chest is
currently there**. Instances parked at `Y <= -10f` are inactive and skipped.

This is the difference between "draw a line to the chest I can already see" (what we ship) and
"know where all the chests are". It's the foundation for anything predictive.

Two caveats:
- The `+0x30` read is a raw struct offset and will break on a ClientStructs/game update. It needs
  a sanity check (row id resolves to a real Treasure row, position is in-bounds) and must fail
  closed rather than draw garbage.
- It filters to SGB 1596/1597 — **bronze and silver only**. Consistent with our own finding that
  the pot gold coffer is an `EventObj` and has no Treasure-sheet row at all.

## 🔴 STOP — the BOCCHI read below was against a STALE checkout (corrected 2026-08-10 late)

The `.cursor/bocchi` checkout was pinned at `ded40a7`, **31 May 2026 — 244 commits behind**.
Upstream has since restructured into per-feature projects (`BOCCHI.Treasure`,
`BOCCHI.Automator`, …) and, critically, **built a full pot-coffer solution that did not exist
when this doc was written**. Now at `993fea1` (10 Aug 2026, "pandora and other treasure things").
Anything below dated 2026-08-01 or the "three things the first pass missed" section describes
code that no longer exists in that form.

### What upstream now has for the pot coffer — and the facts worth taking

`BOCCHI.Automator/StateMachine/Handlers/FarmingPotChestsHandler.cs` +
`Services/PotTreasure/*`. Their approach is NOT triangulation; it is **pre-authored candidate
groups per compass direction**, with a greedy refine and a blind sweep as fallback. Ours is
geometrically better. **Their DATA is better than ours, and that is what to take:**

- **Hints are LogMessage IDs, not text.** No string matching, so it is language-independent:
  `10985` coffer reveal · `10986` hint immediate · `10987` hint close · `10988` hint far ·
  `10989` hint beyond-far · `10990` elixir prompt · `10994` bonus offer.
- **There are FOUR distance bands, not three.** We had immediate / far / far-far and called the
  edges guesses. Upstream reads **Immediate, Close, Far, BeyondFar** and sizes its refine steps
  at **8y / 20y / 40y / 100y**. Our `PotTreasureTriangulation` bands should be rechecked against
  that — a missing middle band would make honest readings contradict.
- **Direction arrives as an int 1–8** (N, NE, E, SE, S, SW, W, NW) in the log message, so the
  compass bearing needs no parsing at all.
- **Status 1531 confirmed** as Cache Me If You Can — and the sheet name is still Eureka's
  "Down the Rabbit Hole", which is why searching by name finds nothing.
- **Magical Elixir = item 2003296.** Reveal coffers are `BaseId 2014741 / 2014742 / 2014743`,
  and upstream filters to exactly those because *layout bronze/silver chests can sit on the same
  spot*.

### ~~This contradicts our "coffer does not exist until you are close" claim~~ — RETRACTED

**I read this wrong on 2026-08-10 and it is corrected here the same day. Do not chase it.**

The claim was: BOCCHI's `FarmingPotChestsHandler.NormalizeY` exists because reveal coffers sit at
Y ≈ -500, therefore the coffer is in the object table parked underground with a readable X/Z,
therefore scanning three BaseIds would solve Problem B outright.

**Y ≈ -500 is a SENTINEL meaning "no valid position", not a real underground placement.** A
second, independent plugin settles it — AOCCH's `TreasureSearchController` (see below) has
`NavmeshSentinelY = -500f` and explicitly *rejects* any position within 0.5 of it with the reason
`"sentinel_elevation"`, alongside its other garbage-coordinate guards. BOCCHI's `NormalizeY`
merely keeps its 2D distance maths working when an object reports that sentinel; it is not
evidence the coffer is findable early.

So our original field note stands unchanged: **the coffer has no real position until you are
within interact range**, no client-side scan can point at it early, and triangulation-style
narrowing remains the only approach. Nothing to test.

### The better reference: Another_Occult_Crescent_Helper (read 2026-08-11)

`https://github.com/baanderson40/Another_Occult_Crescent_Helper` — AGPL-3.0, cloned read-only to
`.cursor/aoch`. **Read it for mechanics and IDs; do NOT copy code — AGPL is copyleft.** This is a
much closer match to our intended design than BOCCHI, and it is a hybrid rather than either pure
approach:

1. **A candidate dataset, but observed rather than invented.** `knowledge-base/{south,north}-horn-
   pot-reveal-positions.json` — **67 South Horn positions, 80 North Horn**, grouped per FATE
   (`persistentPots` 1976 / `pleadingPots` / `secondChance`; `daylightPottery` 2072 /
   `inAPotOfBother` / `secondChance`). Entries carry `positionConfidence`, a `region` name, and a
   `mapCapture` block with the capturing player's position, the nearest threat (name, distance,
   knowledge level) and a timestamp. `positionClusterTolerance: 1.5` yalms merges nearby sightings
   into one spot.
2. **Geometric narrowing over that set** — `GeometricTreasureCandidatePlanner`. Each hint is kept
   as an observation of `(player position, compass direction)`. A candidate is REJECTED if, from
   any observation point, the bearing to it differs from that hint's direction by more than
   `GeometricMaximumHintAngleDegrees` (per-territory, default **95°**, with a 50° alternate).
   Survivors rank by worst angle → summed angle → travel distance. That is cone intersection, the
   same idea as our triangulation, applied to a discrete candidate set instead of a sampled grid.
3. **Finds feed back.** `Telemetry/CofferObservationSubmissionService` posts confirmed
   observations to a Cloudflare D1 worker (`cloudflare/coffer-api`), which accepts only DataIds
   `2014741/2014742/2014743` — independently confirming BOCCHI's three reveal ids.
4. **Useful mechanical fact, stated in their own data:** *"Reveal Data IDs are interchangeable and
   are not candidate identity."* Identity is POSITION, not id — which is why all three ids are
   treated as one set.

**What this means for us.** It vindicates the hybrid the doc already sketched — grid/triangulation
as the primary (works on hunt one, needs no data), with a known-spots accelerator that grows from
finds. Two independent plugins now maintain such a spot list, which is strong evidence that finds
DO repeat, the thing this doc previously listed as unproven. The difference worth keeping is that
ours should *derive* the list from `ChestLedger` rather than ship a hand-curated one, so it can
never be wrong-by-omission — and their 95° default is worth noting as a hint that the per-hint
cone is far wider than our band model assumes.

### Re-read of BOCCHI 2026-08-10 — three things the first pass missed
### ⚠ (this section describes the PRE-RESTRUCTURE code at ded40a7; kept for the layout technique,
### which still exists in `BOCCHI.Treasure/Services/TreasureHunterService.cs`)

Source re-checked at `ded40a7` (`BOCCHI/Modules/Treasure/TreasureHunt.cs`). The layout technique
above is verbatim correct. What was NOT captured:

- **Nodes are Knowledge-Level gated, from a hardcoded table.** `Data/TreasureData.cs` holds
  `Levels` = `Dictionary<Treasure row id, required level>` (~76 entries, values 1–28), and
  `Hunter` filters the route with `GetValidNodes(config.MaxLevel)` —
  `PathfinderConfig.MaxLevel`, an `IntRange(1,28)` defaulting to **23**. So the routing is
  "every chest spot at or below my Knowledge Level", and the level requirement is hand-maintained
  data, not read from the game. If we port this we need that table (or a way to derive it).
- **Spawn detection is separate from the layout.** The layout gives candidate SPOTS; whether a
  chest is actually there comes from the object table (`ObjectKind.Treasure`, `IsTargetable`,
  not dead). Opening is `TargetSystem.Instance()->InteractWithObject`, and completion is read off
  `Treasure.TreasureFlags.Opened` rather than a timer.
- **A null-deref to NOT copy.** `CreatePathfinder` logs `"No active layout"` and then dereferences
  `layout->InstancesByType` anyway; same for the map pointer. Both paths crash rather than
  degrade. Our port must fail closed, which the `+0x30` caveat already demanded.

### The bunny chest is NOT the pot coffer — BOCCHI sidesteps location entirely

Worth stating plainly because the names collide. BOCCHI's other chest system (`Modules/Carrots`)
does not predict anything: it routes to a **Carrot** node from a hardcoded `CarrotData` list
(also level-gated), unmounts, **uses a Fortune Carrot item**, and the chest spawns *there*. Then
it grabs it from the object table by BaseId. You are not finding a chest, you are creating one.

Occult `EventObj` BaseIds from `Enums/OccultObjectType.cs`, useful regardless:
`BunnyChest 2012936`, `KnowledgeCrystal 2007457` (matches ours), `Carrot 2010139`,
`Trap 2014584`, `BigTrap 2014585`.

**BOCCHI has nothing for the pot FATE's hidden coffer** — no elixir-message parsing, no
triangulation, no read of status 1531. Confirmed by search, not assumed. Our Problem B work below
already goes further than upstream, so there is nothing to borrow there.

### Dispatch-model difference (not a bug, but do not copy it)

BOCCHI casts phantom actions as `ActionType.GeneralAction` rows 31/32/33. Those resolve to
**"Phantom Action I / II / III"** — they are the duty-bar SLOTS, not specific actions, so its
`Actions.Freelancer` mapping (31 Resuscitation, 32 Treasuresight, 33 Inquiring Mind) silently
assumes a particular bar order. Daedalus presses real action ids through the scheduler, which is
slot-order independent and strictly better. Note this also means **BOCCHI does not tell us
Inquiring Mind's action id** — that question stays open.

## ⚠ Field finding 2026-08-01: world coffer tier is PER SPAWN

A world coffer spot produced **silver on one visit and bronze on another** — observed directly,
not inferred from our SGB read, so it isn't a detection artifact.

Consequences:

- **Location does not predict tier for world coffers.** The best any predictor can offer is a
  distribution ("this spot has gone bronze twice and silver once"), never a guarantee. Phase 1's
  layout scan still tells you where chests *can* be, which remains useful — it just can't tell
  you what they'll be worth.
- The ledger was rebuilt for exactly this: entries carry a `TierCounts` distribution instead of a
  single tier, because the original design silently overwrote conflicting samples and would have
  hidden this.
- **It does NOT automatically disprove the per-spot claim for POT coffers.** Those are a
  different mechanism — EventObj rather than Treasure-sheet objects, awarded by a FATE rather
  than found in the world. The claim in `PotFateTracker` that North Horn's northern spot pays
  gold and its southern spot bronze rests on a handful of samples and is now under more
  suspicion, but it has not been tested directly. Treat it as unproven either way.

## Problem A — tier prediction (do this first)

Everything needed is already in the repo:

- Field evidence in `PotFateTracker`: tier looks **spot-bound**, not zone-bound. Inside North
  Horn the northern spot (Daylight Pottery) produced gold coffers and the southern one (In a Pot
  of Bother) produced bronze.
- We just shipped the spot labels (`PotFateTracker.SpotLabels`), so the HUD already names which
  spot a FATE is.

So the feature is: alongside "Daylight Pottery (north pots): ~12:34", state the expected tier and
the confidence behind it — "gold expected (2 of 2 observed)".

That needs an **observation ledger**, not a hardcoded table. Record every pot coffer opened:
zone, FATE name, tier. Predict from the record, and say how many samples back it. Two samples is
a hint, not a law — and an earlier note in `PotFateTracker` was wrong precisely because it
generalised from one sample.

Persist it the same way the sighting history now persists, but **do not clear it on leaving the
zone** — unlike the spawn timer, tier evidence accumulates across visits and instances.

## ✅ Problem B is SOLVED in principle — it's triangulation (2026-08-01)

The mechanic, from the field:

- The elixir reports a **direction and a distance band**:
  `You sense something [far, far | far | immediately] to the <compass direction>.`
  The hunt ends with `You discover a treasure coffer!`
- **The coffer does not exist as an object until you are within interact range.** This settles
  the question this doc previously listed as unknown #1: no client-side detection can ever point
  at it early, the layout scan cannot see it, and Treasuresight is irrelevant to finding it. Our
  chest lines only ever draw it once you are already on top of it.
- Therefore triangulation is not the best approach, it is the **only** one.

Each reading is a ring segment — a cone from where you stood, bounded by the distance band. Two
readings from well-separated spots intersect; a few collapse the region to something sweepable.

Built and tested: `Services/Occult/PotTreasureTriangulation.cs` (pure geometry, no game state) —
message parsing against the real wording, cone + band containment, feasible-set filtering,
centre estimation over a sampled grid, `CrossingQuality` to advise when another reading is worth
taking, and `Calibrate` to recover the true band edges from a find.

**Only "immediately" (<10y) is a confirmed distance.** The other bands are guesses and the
windows deliberately overlap, because bands that are too tight make honest readings contradict
and yield nothing, whereas over-wide ones merely cost search area. Every completed hunt yields
ground truth via `Calibrate`, so the edges are recoverable from data.

### Implementation plan

**The candidate list cannot be seeded up front.** The coffer has no object until you are on top
of it, so "all possible spawns" does not exist to be trimmed. Two sources instead, and the engine
takes either:

- **Grid** (`EstimateCentre`) — sample points over the search area and keep what satisfies every
  reading. Works from the very first hunt, needs no history. This is what v1 must use.
- **Known spots** (`Feasible`) — past finds, via `ChestLedger.IsPotHuntCandidate`, which requires
  BOTH the hunt flag AND `Source == EventObj`. Never filter on the hunt flag alone: it is set on
  any coffer seen while the hunt is up, so an ordinary per-player chest walked past mid-hunt
  would otherwise be treated as a candidate spawn.
  Only becomes useful once hunts accumulate AND finds repeat, which is still unproven. Treat as
  an accelerator layered on the grid, never as the primary.

So the trimming model is right; the starting set is a grid, not a spawn list.

**P1 — `PotTreasureHunt` service.** Subscribe to chat. Feed every line through `TryReadElixir`
with the player's live position; append the bearing. Reset the set on `IsDiscovery`, on status
1531 dropping, and on zone change. On discovery, run `Calibrate` and `AllReadingsAgreeWith` and
store the samples. No UI — testable on its own, and it starts gathering band calibration data
immediately.

**P2 — when to show what.** Trigger on whether the region is BOUNDED and small, not on reading
count or band. `far, far` has no maximum distance, so one such reading is an unbounded wedge and
worth nothing on a map — a compass arrow and the band text say everything. `far` and `within` are
bounded on the first reading (20-80y, 0-30y) and earn a map straight away, as do any two crossing
cones. Deferring the map until the final band gets this backwards: at `immediately` you are
within 10y and the coffer spawns as you approach, so a picture adds least there.

At `immediately`, drop the map and **draw the estimate in the world** using the same
`DrawingService` calls as the chest guide lines — a ring at the estimated position. Two rules:

- It must look clearly different from a real coffer ring. We are drawing a prediction for an
  object that does not exist yet, and if it looks like a sighting it will be trusted like one.
- It must vanish the moment the real coffer spawns, or there are two markers a few yalms apart
  with no way to tell which is which. That handoff doubles as a free accuracy check: prediction
  and reality landing together confirms the geometry, and a consistent offset is calibration
  data you can see rather than infer.

Make that ring the **activation area** rather than a bare marker: a crimson circle centred on the
estimate, with the radius you have to be within for the coffer to spawn. One circle then says
both "it is about here" and "stand inside this and it appears", and crimson cannot be confused
with the gold/silver/bronze of a real coffer. Snap it to the floor like the other rings.

The activation radius is UNKNOWN and should not be guessed at tightly — a circle drawn too small
is the bad direction to be wrong in, because you walk to its edge, nothing spawns, and you
conclude the prediction failed when you were merely out of range. Err generous, and MEASURE it:
when the coffer spawns, record the player's distance to it at that instant. That is the
activation radius observed rather than assumed, and a few hunts pin it exactly — the same trick
`Calibrate` uses for the distance bands.

Build it always-on behind a dev toggle FIRST. Watching the region shrink across a real hunt is
how the geometry and the guessed bands get validated; debugging a predictor you cannot see is
miserable. Apply the bounded-and-small trigger once it is proven.

Draw band edges softly rather than as hard lines — the direction and overlap geometry are
trustworthy, the distances are not until `Calibrate` has samples, and a crisp edge implies
precision we do not have.

The map itself, when shown — a small window, top-down, autoscaled to the feasible region:
- player position and facing
- each reading as a ring segment from where it was taken
- the surviving grid cells (the overlap) as a shaded region, shrinking with each reading
- a suggested walk-to point (the centroid) and `CrossingQuality` advice — "walk further before
  reading again" when the next reading would barely narrow anything
- world-space drawing via `DrawingService` is optional and secondary; the 2D map is the feature

**P3 — ledger priors.** Once several hunts are logged, overlay hunt-tagged spots that survive the
readings. If finds repeat, this collapses the search instantly; if they never repeat, the overlay
simply stays empty and costs nothing — and that itself answers whether spawn points are fixed.

**P4 — close the calibration loop.** With enough `Calibrate` samples, replace the guessed band
edges with measured ones. Watch `AllReadingsAgreeWith`: a find outside its own readings means the
arc is narrower than 22.5° or a band edge is wrong.

### Edge cases to handle in P1

- Only the local player's messages count — a party member's elixir must not add bearings.
- A reading taken while moving records the position at message time; close enough, but it is why
  the arc should stay generous.
- Elixir use may be limited per hunt; if so, `CrossingQuality` matters more, since a wasted
  reading from a bad spot is expensive.
- Two hunts back to back must not share bearings — hence resetting on discovery, not just on
  status loss.

## Problem B — original notes (superseded by the section above)

Honest position: **we do not know how the hunt works mechanically**, so this cannot start as
prediction.

What we know:
- The Gold Coffer is `ObjectKind.EventObj`, BaseId **2014741**, targetable, named "Gold Coffer"
  (field-confirmed 2026-07-31). Our chest lines already draw to it once it exists as an object.
- `PotFateTracker.TreasureHuntStatusId` = 1531 ("Cache Me if You Can") marks the hunt.
- The layout scan above will NOT find it — it isn't a Treasure-sheet object.

What we don't know, and must find out before designing anything:
1. Does the coffer object exist (in the object table, or in the layout) **before** it is revealed?
   If it does, this is trivial — draw the line and we're done. If it doesn't, no client-side
   prediction can point at it.
2. Does the elixir give a directional or proximity signal we can read (status parameter, chat
   message, map marker)?
3. Are hidden coffers drawn from a **fixed candidate set** per FATE spot? If so, logging finds
   becomes a real predictor after enough samples.

The first step is therefore instrumentation, not prediction: while status 1531 is up, log the
player's position, any object appearing with BaseId 2014741, and the position where the coffer is
eventually found. After a handful of hunts the shape of the mechanic will be obvious.

**`Occult Treasuresight`** (action 41651, Freelancer Lv10, already in our catalog) is worth
testing here — BOCCHI casts it deliberately (switching to Freelancer to do so) as a reveal. If it
reveals the hidden pot coffer as an object, question 1 answers itself and B collapses into a
much easier feature.

## Proposed phases

1. **Layout reader** — enumerate treasure spawn points + tiers from `LayoutWorld`, with the
   offset sanity-checked and failing closed. Surfaces as guide lines to *potential* chest spots,
   distinct in colour from live coffers. Testable: the tier mapping is already a pure function.
2. **Tier ledger** — record pot coffer tier per (zone, FATE spot), persisted and never
   zone-cleared; HUD shows expected tier with sample count.
3. **Hunt instrumentation** — log everything during status 1531. No prediction, just evidence.
4. **Treasuresight test** — does 41651 reveal the hidden coffer? One field session answers it.
5. **Predictor proper** — only if 3 and 4 show there's something to predict.

Phases 1 and 2 are independently useful and don't depend on the unknowns. Phases 3-5 are a
research loop, and 5 may turn out to be impossible — that's an acceptable outcome, and better
than guessing at it now.

## Which did you mean?

If you meant B specifically (find the hidden coffer during the hunt), phases 3 and 4 are the real
work and phase 1 is optional. If you meant A (know the payout before travelling), phase 2 alone
delivers it and the rest is bonus.
