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

## Problem B — locating the hidden pot coffer

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
