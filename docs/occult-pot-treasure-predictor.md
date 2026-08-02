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
- **Known spots** (`Feasible`) — past finds, from ledger entries with `FoundDuringTreasureHunt`.
  Only becomes useful once hunts accumulate AND finds repeat, which is still unproven. Treat as
  an accelerator layered on the grid, never as the primary.

So the trimming model is right; the starting set is a grid, not a spawn list.

**P1 — `PotTreasureHunt` service.** Subscribe to chat. Feed every line through `TryReadElixir`
with the player's live position; append the bearing. Reset the set on `IsDiscovery`, on status
1531 dropping, and on zone change. On discovery, run `Calibrate` and `AllReadingsAgreeWith` and
store the samples. No UI — testable on its own, and it starts gathering band calibration data
immediately.

**P2 — the map.** A small window, top-down, autoscaled to the feasible region:
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
