# What Minerva exposes to Daedalus

Written 2026-09-01 from the Minerva side, after reading which `BossMod.*` gates Daedalus already calls and
how its targeting service picks. Minerva is the boss-module / auto-dodge plugin (`D:\Dev\Minerva`); this is
its provider surface: **36 call gates, 3 shared-data tags**.

Minerva owns *where is safe, when damage lands, and what the fight wants attacked*. It does not do
rotation and is not going to — that is Daedalus's job.

**Why this surface is wider than BossmodReborn's.** Minerva exists to help run a **boxed** party, so
everything it knows has to reach whatever is driving each character. A person reading a radar can act on
"Provoke!" printed on their screen; a plugin driving two tanks cannot. Several gates below have no BMR
equivalent for exactly that reason — BMR has the knowledge and renders it as text.

---

## 1. Fast path — shared data, no IPC, no exceptions

Three `bool[]` tags, republished every frame, readable with no try/catch cost (same pattern as
`ariadne.PathIsRunning`):

```csharp
var stop = pi.TryGetData<bool[]>("minerva.MustNotAct", out var f) && f is { Length: > 0 } && f[0];
```

| tag | `flag[0]` is true while… |
|---|---|
| `minerva.MustNotAct` | any action would punish (Pyretic, Motion-Tracker-style mechanics) |
| `minerva.MustNotMove` | movement would punish |
| `minerva.MustNotTurn` | a gaze constrains facing |

`MustNotAct` is worth wiring first. Several fights punish *acting*, not moving, and a rotation driven by a
hardcoded status-id list misses each new one. Reading this means Daedalus stops for whatever the active
module says is a stand-still punisher — including cases driven by beam overlap rather than by a status,
which no status list can catch.

`MustNotTurn` is subtler: the game turns you toward your target when you act, so continuing to cast
*undoes* the turn away from a gaze. Holding here is not about the action being punished — it is about not
being spun back into the beam.

---

## 2. Timing — what is about to happen

| gate | signature | meaning |
|---|---|---|
| `Minerva.MaxCastTime` | `() -> float` | **how long you may stand casting before you must move** |
| `Minerva.Hints.SecondsUntilRaidwide` | `() -> float` | next raidwide |
| `Minerva.Hints.SecondsUntilTankbuster` | `() -> float` | next tankbuster |
| `Minerva.Hints.SecondsUntilSharedDamage` | `() -> float` | next stack / shared hit |
| `Minerva.SecondsUntilMustNotAct` | `() -> float` | until acting starts punishing |
| `Minerva.SecondsUntilMustNotMove` | `() -> float` | until moving starts punishing |
| `Minerva.SecondsUntilGaze` | `() -> float` | until facing is snapshotted |

**`NaN` means "nothing known to be coming"** throughout — one `float.IsNaN` rather than an agreed magic
number.

These exist because the flags in §1 are **present tense**, and a rotation reading only present tense reacts
a GCD too late: it has already committed a hardcast that resolves inside the mechanic. Lead time lets it
decline to start rather than be interrupted. Call gates rather than shared data on purpose — a rotation
asks at GCD boundaries, not every frame.

**For healing prediction:** `SecondsUntilRaidwide` is the pre-shield signal; `SecondsUntilSharedDamage`
covers stacks, which want mitigation aimed differently.

---

## 2b. Damage already announced — the healing signal

| gate | signature | meaning |
|---|---|---|
| `Minerva.Party.PendingHP` | `() -> int[]` | each party slot's HP **after** announced effects land; `int.MinValue` = empty slot |
| `Minerva.Hints.PendingHPRaw` | `(ulong actorId) -> int` | one actor's HP after they land; may go **below zero** |
| `Minerva.Hints.PendingHPDifference` | `(ulong actorId) -> int` | net inbound change; negative for damage |
| `Minerva.Hints.PendingStatuses` | `(ulong actorId) -> uint[]` | status ids announced but not applied |

**This is the sharpest healing signal Minerva has.** The timers in §2 say *something is coming*; these say
it has **landed** — on whom, for how much — while the HP bar still reads full. The server confirms a
resolved action before the client applies it, and inside that window the hit is a fact rather than a
forecast. Reacting after the bar moves is reacting after the information existed.

```csharp
var hp = partyPendingHP.InvokeFunc();       // by party slot
for (var i = 0; i < hp.Length; i++)
{
    if (hp[i] == int.MinValue) continue;    // empty slot
    if (hp[i] <= 0) EmergencyRaiseWatch(i); // already dead, just not drawn yet
    else if (hp[i] < threshold) Heal(i);
}
```

`PendingHPRaw` going **below zero** is the point: it is how "this hit is lethal" becomes answerable before
the hit is drawn, which no HP read can tell you.

**Pruned by expiry, not confirmation** — three seconds, matching BossmodReborn. Some effects never confirm
at all (overkill damage, a heal into a full bar, a reapplied buff), so a list waiting on confirmation would
leak rather than settle. The practical consequence: treat these as a short-lived window, not as state.

**0 means "nothing inbound" and also "actor unknown"** — deliberately indistinguishable, because both mean
"no reason to act". Party slots are the game's party-list order; the local player is not reliably slot 0.

## 2c. Cleansing — who, and whether it is already handled

| gate | signature | meaning |
|---|---|---|
| `Minerva.Party.CleanseTargets` | `() -> int[]` | party SLOTS the fight wants cleansed; empty = none |
| `Minerva.Hints.CleansePending` | `(ulong actorId) -> bool` | a cleanse is already in flight on this actor |

**These answer a different question from "who has a debuff".** A rotation can already read status ids; what
it cannot tell is which of the dozens on the bar matters *in this fight*. `CleanseTargets` is the module
saying so, and empty means nothing needs cleansing rather than nothing is known.

The second gate exists because of boxing specifically. A status stays on the bar until the cleanse
RESOLVES, so four toons reading "still debuffed" will each throw an Esuna at it:

```csharp
foreach (var slot in cleanseTargets.InvokeFunc())
{
    var actor = PartyActorId(slot);
    if (!cleansePending.InvokeFunc(actor))   // someone else's Esuna is already inbound
        Esuna(actor);
}
```

Backed by the same announced-but-not-drawn window as §2b, so it expires by itself in three seconds — treat
it as a short-lived interlock, not as state.

## 3. Tanking — busters and swaps

| gate | signature | meaning |
|---|---|---|
| `Minerva.Hints.NextTankbusterTargets` | `() -> uint` | **party-slot bitmask of who the buster is aimed at**; 0 = unknown |
| `Minerva.Hints.TankSwapCurrentTank` | `() -> ulong` | **instance ID of the tank who must hand over**; 0 = no swap due |
| `Minerva.Hints.SecondsUntilTankSwap` | `() -> float` | deadline for that swap; `NaN` = none due |

A timer alone only supports mitigation. Both of these answer **who**, which is what a swap is decided from.

```csharp
// mitigation
var inSec = secondsUntilTankbuster.InvokeFunc();
var mask  = nextTankbusterTargets.InvokeFunc();
var onMe  = (mask & (1u << mySlot)) != 0;

// the swap itself
var holder = tankSwapCurrentTank.InvokeFunc();       // 0 = no swap due
if (holder != 0)
{
    var by = secondsUntilTankSwap.InvokeFunc();
    if (myId == holder) DropAggro();                 // Shirk, or stop attacking
    else if (imATank)   Provoke();
}
```

BossmodReborn has the swap component and prints *"Provoke!"* / *"Pass aggro!"* at whichever character is
being read. It is **not exposed over its IPC at all**, and the words cannot be acted on when one plugin
drives both tanks — something has to choose which character drops and which takes. The identity is the
decision.

`NextTankbusterTargets` uses Minerva **party slots**, which are the game's party-list order.
**The local player is not reliably slot 0.** That assumption was wrong inside Minerva itself and cost a
session's worth of misattributed analysis before a stray vuln stack exposed it. Resolve your own slot.

---

## 4. Targeting — constraints, not a replacement

| gate | signature | meaning |
|---|---|---|
| `Minerva.Hints.ForcedTarget` | `() -> ulong` | **hard target**: attack this; 0 = no opinion |
| `Minerva.Hints.PriorityTargets` | `() -> ulong[]` | attack ahead of everything else, best first; **empty = no opinion** |
| `Minerva.Hints.DeprioritizedTargets` | `() -> ulong[]` | legal but wasteful — leave for last |
| `Minerva.Hints.ForbiddenTargets` | `() -> ulong[]` | invincible or forbidden — never attack |
| `Minerva.Hints.TargetsToInterrupt` | `() -> ulong[]` | whose current cast to interrupt |
| `Minerva.Hints.TargetsToStun` | `() -> ulong[]` | whom to stun |

**Quest battles are the clearest case.** A solo duty routinely requires killing adds in an order, ignoring
a boss until a phase ends, or focusing one specific mob — requirements that exist only in that fight's
script. Minerva reads them from the module; nothing in a rotation's own view of hostiles and HP can.

Daedalus already picks among candidates (`LowestHp`, `Nearest`, `FollowTank`) and filters invulnerables.
Minerva does not replace that — it supplies what the *fight* knows and a rotation cannot derive:

```csharp
var forced = forcedTarget.InvokeFunc();
if (forced != 0) { Attack(forced); return; }     // the fight demands this one

var never = forbiddenTargets.InvokeFunc();       // decoys, phase-invulnerable bosses
var first = priorityTargets.InvokeFunc();        // empty = no opinion
var last  = deprioritizedTargets.InvokeFunc();

// your own strategy over (candidates minus never), weighted by first / last
```

**Empty means "no opinion", not "nothing to attack".** Every hostile is seeded internally at priority 0, so
returning the whole list would hand you an ordering Minerva never meant — and you could not tell an
authored opinion from a default. `PriorityTargets` contains only enemies a module explicitly raised.

`ForbiddenTargets` is worth wiring even if you keep your own selection entirely: it is fight-authored
knowledge that complements `EnableInvulnerabilityFiltering`, catching a boss invulnerable *for this phase
only* or an add that is a decoy — neither of which reads as invulnerable from status flags.

---

## 5. Position and facing

| gate | signature | meaning |
|---|---|---|
| `Minerva.Hints.IsPositionSafe` | `(Vector3 to) -> bool` | is that spot clear **right now** |
| `Minerva.Hints.IsDashSafe` | `(Vector3 from, Vector3 to) -> bool` | as above, and the line does not leave the arena |
| `Minerva.SafeFacing` | `() -> float` | radians, game convention; `NaN` when facing is unconstrained |
| `Minerva.RequestPositional` | `(int mask, double seconds) -> bool` | ask the dodge to prefer flank/rear |

### The revival case

Two different questions, and a raise needs both:

```csharp
var spotOk   = isPositionSafe.InvokeFunc(bodyPos);   // clear RIGHT NOW
var window   = maxCastTime.InvokeFunc();             // how long it STAYS clear
var canRaise = spotOk && window >= raiseCastTime + latencyMargin;
```

**`IsPositionSafe` is not time-aware.** It answers about the forbidden zones that exist at this instant,
matching BMR's semantics exactly. `MaxCastTime` is the one that knows how long standing there stays good.
Reading the first as though it were the second is how a raise gets started one second before a mechanic
lands — the single easiest mistake against this surface.

`SecondsUntilMustNotMove` is the companion for a raise already in progress: it says how long before
Minerva wants to move the character, which is when the cast gets dropped.

`RequestPositional` expires on its own rather than needing a matching release, so a rotation that swaps
target, dies, or is switched off mid-GCD cannot pin the character behind a boss forever. Re-assert it each
time you still want it; a dropped call is harmless.

---

## 6. State and presets

| gate | signature | meaning |
|---|---|---|
| `Minerva.IsConnected` | `() -> bool` | provider alive |
| `Minerva.ActiveModule` | `() -> string` | active boss module's type name; `""` when none |
| `Minerva.MustNotAct` | `() -> bool` | same truth as the shared-data flag |
| `Minerva.MustNotMove` | `() -> bool` | same truth as the shared-data flag |
| `Minerva.MustNotTurn` | `() -> bool` | same truth as the shared-data flag |
| `Minerva.ListPresets` | `() -> string[]` | available dodge presets |
| `Minerva.ActivePreset` | `() -> string` | preset in force |
| `Minerva.ApplyPreset` | `(string preset, string owner) -> bool` | borrow a preset |
| `Minerva.ReleasePreset` | `(string owner) -> bool` | give it back |

---

## Mapping from the `BossMod.*` gates Daedalus already calls

| BossmodReborn | Minerva | note |
|---|---|---|
| `Hints.IsPositionSafe` | `Minerva.Hints.IsPositionSafe` | same shape, same semantics |
| `Hints.IsDashSafe` | `Minerva.Hints.IsDashSafe` | same shape, same semantics |
| `Hints.NextDamageIn` | `Minerva.Hints.SecondsUntilRaidwide` | `NaN` instead of a magic number |
| `Hints.NextTankbusterDamageIn` | `Minerva.Hints.SecondsUntilTankbuster` | plus `NextTankbusterTargets` |
| `ActiveModuleName` | `Minerva.ActiveModule` | |
| `HasActiveModule` | `Minerva.ActiveModule` | non-empty string |
| *(no equivalent)* | `Minerva.MaxCastTime` | BMR calls this `Hints.MaxCastTime` |
| *(hint text only)* | `Minerva.Hints.TankSwapCurrentTank` | BMR prints "Provoke!"; never exposed |
| *(no equivalent)* | `Minerva.Hints.NextTankbusterTargets` | BMR says when, never at whom |

**Not exposed yet** — short work, simply not published:

- `Hints.ForbiddenZonesCount`, `Hints.ForbiddenZonesNextActivation`
- `AI.NaviTargetPos`, `AI.IsNavigating` — Minerva's movement state is not published. Minerva stands down
  entirely while Ariadne's `PathIsRunning` flag is set, so "is something moving me" is partly answerable
  from Ariadne today.
- `AI.PauseMovement` — **now covered, by `Minerva.RequestHold` below.** Not a rename: read the contract.

### `Minerva.RequestHold` `(double seconds) -> bool`

**This is what makes a hardcast raise work under auto-dodge.** `MaxCastTime` answers *"may I stand here"*
honestly, including reading **0** whenever Minerva is steering the character — and Regain steers whenever
you are out of the uptime band with nothing dangerous anywhere. A healer walked to a corpse 30y from the
boss is permanently out of that band, so without this gate `MaxCastTime` reads 0 forever, `RaiseCastHold`
declines forever, and the raise never happens. The refusal is legible rather than silent, which is an
improvement, but it is still a refusal.

The reason is not that either side is wrong. Daedalus is out of position **on purpose** and Minerva has no
way to tell that from drifting. Nothing but saying so settles it:

```csharp
// each frame the raise is wanted -- re-assert, do not fire once
requestHold.InvokeFunc(3.0);
if (maxCastTime.InvokeFunc() >= castSeconds) StartRaise();
```

**Only uptime movement yields. Danger still moves the character.** If the ground underfoot is going to kill
them the hold is ignored and the dodge runs, because a raiser who dies mid-cast has raised nobody — a hold
that could pin someone in an AOE would cost more than the stall it fixes. This is deliberately narrower
than `BossMod.AI.PauseMovement`, which stops everything; `RaiseCastHold`'s own note already flags that as a
risk it accepts. **So `MaxCastTime` can still drop to 0 while a hold is active** — that means an AOE is
coming, not that the hold was ignored. Re-check it rather than assuming the hold bought the whole cast.

**Expires by itself**, capped at 30s, exactly like `RequestPositional` and for the same reason: a caller
that crashes or abandons the cast must not be able to park a character out of position indefinitely.
Re-assert each frame. A later call *replaces* the deadline rather than extending it, so a caller can also
shorten its own hold by asking for less. `RaiseCastHold`'s expiry-driven shape maps onto this directly —
same lifetime model, so the swap is at the pump, not in the raise logic.

**Out of scope, deliberately:** `BossMod.Autorotation.*`, `BossMod.Presets.*`, `BossMod.Configuration`.
Minerva will not grow an autorotation surface. `Minerva.RequestPositional` is the intended seam: Daedalus
states what its next weaponskill wants, Minerva honours it where safety allows and ignores it where it does
not. Neither side can answer that alone.

---

## Duty actions — tracked, not yet exposed

Minerva now reads the duty's granted actions every tick into `WorldState.Client.DutyActions` (five slots;
only 0-1 carry charges, because the game added slots 3-5 without extending the charge arrays). **There is
no IPC gate for it yet** — flagging it because it is squarely Daedalus's problem, not Minerva's.

Duty actions are what a fight hands you **when it changes what you are**: Wuk Lamat's own kit in a
Dawntrail solo duty, a Bozja or Eureka lost action, the single button left while transformed. In those
fights the duty action is sometimes the *only* usable thing — one ported module notes it is the only thing
castable on Fordola without being stunned — and nothing about knowing your job tells a rotation that. A
job-driven rotation in a transformed duty has no correct answer available to it.

If Daedalus wants it, the gate is small on this side:

```
Minerva.Client.DutyActions      () -> uint[]   action ids by slot, 0 where empty
Minerva.Client.DutyActionCharges() -> uint[]   packed cur/max, slots 0-1 only
```

Say the word and it goes in. It is not there today only because nothing has asked, and a gate with no
consumer is a gate nobody notices has broken.

## Coverage — read this before trusting a silent answer

Several gates were wired on 2026-09-01, and in each case the *data* had to be fixed as well as published,
because the knowledge stopped at a text hint:

| surface | was | now |
|---|---|---|
| tankbusters | `SingleTargetCast` printed "Tankbuster" and recorded nothing, in **240+ modules** | publishes predicted damage with the target's slot |
| targeting | `AIHints.PotentialTargets` was never populated, so **40 modules** set priorities on an empty list | seeded from the world each frame, before components run |
| tank swaps | text only, as in BMR | publishes the holder's identity; **7 modules** use the component |

The consequence for a consumer: **you cannot distinguish "nothing is coming" from "nobody told me"** — both
are `NaN`, `0`, or an empty array. If a fight you expect an answer from stays silent, that is a module
missing the relevant component, not a fight without the mechanic. Report it back rather than working around
it; the fix belongs in the module. `SharedTankbuster` currently has **0** modules using it, so shared-buster
fights stay silent until one does.

## Other gotchas

- Every gate is safe to call with no module active. Safety queries return `true` (nothing known to be
  forbidden), lists come back empty, timers come back `NaN`.
- Instance IDs are game object IDs. Party slots are the game's party-list order — again, **not** POV-first.
- Minerva re-solves every frame; all of these are current-frame answers with no caching.
- None of this has been verified in game yet. The plumbing is tested headless (682 self-tests, 151
  recordings validated); the gates themselves have had no live consumer.
