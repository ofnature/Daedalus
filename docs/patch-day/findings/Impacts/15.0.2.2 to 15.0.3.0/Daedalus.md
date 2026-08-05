# Daedalus — Impact: Dalamud 15.0.2.2 → 15.0.3.0

- Source root (verified): `D:\Dev\Olympus` — project `Daedalus\Daedalus.csproj`,
  tests `Daedalus.Tests`.
- Baseline → endpoint: `15.0.2.2`/`4a6abae2` → `15.0.3.0` (CS `82f00f3f` →
  `cc474ca9`, Lumina pkg 7.5.0 → 7.6.0, EXDSchema → `cf037c37`).
- Metadata: plugin v0.1.45, `Dalamud.NET.Sdk/15.0.0`, manifest API level 15,
  net9.0-windows. Build baseline: 18 warnings, 3095 tests.

## Scan counts and direct code surfaces

- Native hooks: **3** — all in `Services/CombatEventService.cs` (DPS parser):
  `HookFromAddress<ActionEffectHandler.Delegates.Receive>` (line 219),
  `HookFromSignature<ProcessActorControlDelegate>` (line 232),
  `HookFromSignature<AddScreenLogDelegate>` (line 245). Only plugin in the
  fleet with native hooks.
- ClientStructs reads: `DutyActionManager` (occult/variant action slots),
  `PublicContentOccultCrescent.GetInstance/GetState`, `ContentDirector`
  (knowledge-level fallback), `TerritoryInfo`/`TerritoryIntendedUse`, `UIState`,
  `PlayerState` (`GearSnapshotService`, `PlayerStatsService`, farm helpers),
  `AgentMap`, `AtkUnitBase`/AtkValues (MKDInfo knowledge-level read),
  `GameObject` (incl. `NamePlateIconId` via generated property), `GameMain`,
  `BGCollision`. `CharacterData` grep hits are FFLogs REST DTOs, not CS.
- Lumina: `Item` sheet ×4 (meld optimizer HQ stats/`BaseParamSpecial`, occult
  item names, farm mode). Item's schema changed in Delta A.
- Texture/fonts: 8 files touch `ITextureProvider`/FontAtlas surfaces.
- ConfigChangeEvent / SigScanner / drag-drop / `UnlockState` / async plugin:
  **no consumers**.

## Current runtime-tag requirements

1. Rebuild **both** Debug and Release against the 15.0.3.0 dev libs (repo rule).
2. **Warning-count gate**: Dalamud enabled nullable reference types everywhere;
   new nullability annotations can push warnings past the hard 18-warning
   baseline. Treat any increase as a fix-before-commit item.
3. Hook smoke: the SafetyHook backend (#2892) + Reloaded.Hooks allocator bump
   (#2898) change hook plumbing. Run the DPS parser: damage lines, DoT tick
   channel (ActorControl cat 1541), screen-log entries.
4. CS read smoke: Occult Debug ▸ Duty tab in South Horn (job/level/slots/KL/
   currencies in one view — covers DutyActionManager, occult state, MKDInfo,
   TerritoryInfo at once); melding tab digit-check vs Character window (Item
   schema); hunt-mode gold-icon targeting (`GameObject`).
5. Reload smoke: several enable/disable/reload cycles — the shutdown/unload
   rework (#2857/#2896/#2897) changed exactly the path the Debug auto-reload
   workflow exercises constantly. Watch for unload hangs or double-dispose logs.

## Post-tag watch items

- Upstream added an Occult Crescent state null-guard (`372d94f2`) — the occult
  native area is churning; keep the Duty tab canary habit each bump.
- `IFramework.CreateDebouncer`/`IDebouncer` (new, additive) — candidate for
  replacing hand-rolled debounce timers later; no action now.

## Impact assessment

**Medium-high.** No API migration, but Daedalus has the fleet's only native
hooks (two signature-based — signatures can silently rot across client/CS
bumps), the widest CS read surface, a hard warning baseline that
nullable-everywhere can break, and a reload-heavy dev workflow sitting right on
the reworked shutdown path.

## Before > After Codebase Examples

### 1. Signature hooks must fail loud, not silent (`Services/CombatEventService.cs:232,245`)

Before (fragile pattern to search for):

```csharp
actorControlHook = gameInterop.HookFromSignature<ProcessActorControlDelegate>(sig, Detour);
actorControlHook.Enable(); // NRE or silent no-op if the signature stopped matching
```

After (current safer pattern):

```csharp
try
{
    actorControlHook = gameInterop.HookFromSignature<ProcessActorControlDelegate>(sig, Detour);
    actorControlHook?.Enable();
}
catch (Exception ex)
{
    log.Error(ex, "ActorControl hook failed — DPS parser tick channel disabled");
}
```

Upstream evidence: SafetyHook backend (#2892), Reloaded allocator bump (#2898),
plus a fresh client build behind CS `cc474ca9` (signatures re-resolve against
new code). **Daedalus already wraps hook creation in try/catch — validation
target, not an edit**: confirm the parser logs show all three hooks enabled
after rebuild.

### 2. Occult state reads null-guard (`Services/Occult/*` readers)

Before (search-for pattern):

```csharp
var state = PublicContentOccultCrescent.GetState(); // deref without guard
var level = state->KnowledgeLevel;
```

After:

```csharp
var instance = PublicContentOccultCrescent.GetInstance();
if (instance == null) return null;
var state = PublicContentOccultCrescent.GetState();
if (state == null) return null;
```

Upstream evidence: Dalamud itself added "Return early if Occult Crescent state
is null" (`372d94f2`) — the struct can legitimately be null in more states than
assumed. **Daedalus readers already null-guard — validation target**: re-verify
in-zone and out-of-zone reads after rebuild.

### 3. Item sheet rows via explicit-miss access (meld optimizer / farm)

Before:

```csharp
var row = itemSheet.GetRow(itemId);
Use(row.BaseParamSpecial0); // assumes generated shape is stable
```

After:

```csharp
if (!itemSheet.TryGetRow(itemId, out var row)) return;
Use(row.BaseParamSpecial0); // field meaning re-validated after schema bump
```

Upstream evidence: Item schema changed in Lumina 7.6.0 (Delta A, filter/repair
metadata). The named stat fields we consume are unlikely to move, but the
validation is a digit-exact compare of the Melding tab vs the in-game Character
window.

### 4. Reload lifecycle — dispose order (`Plugin.cs`, service teardown)

Before (search-for pattern):

```csharp
public void Dispose()
{
    // hooks/windows disposed in ad-hoc order, no idempotency
}
```

After:

```csharp
public void Dispose()
{
    actorControlHook?.Disable();
    actorControlHook?.Dispose(); // idempotent, hooks before services they feed
    ...
}
```

Upstream evidence: shutdown rework — `PluginManager.Dispose` now unloads
plugins, framework destroy hook is global, texture/font work cancels on
shutdown (#2857/#2896/#2897). **Validation target**: run repeated Debug
auto-reload cycles and a full game exit; look for hang, double-dispose, or
texture errors in the Dalamud log.

## Current result

Rebuilt 2026-07-28 against the 15.0.3.0 dev libs (updated locally 17:48):
Debug and Release both **0 errors**; full test suite **4266/4266 passed**.
Plugin warning count moved 18 → 20 — the two new warnings are both
**CS0618: `IGameObject.YalmDistanceX` is obsolete, use `CurrentDistance`**
(`Services/Targeting/TargetingService.cs:1164` and `:1369`), a 15.0.3.0
deprecation riding the CS target-distance changes. Two-line migration, not yet
applied (per implementation boundary). All other warnings match the prior
baseline.

## In-game validation (2026-07-28, user-confirmed on the 15.0.3.0 client)

- **Parser hooks: PASS** — DPS parser working live (all three native hooks
  resolve and fire under the new SafetyHook/Reloaded plumbing).
- **Melding: PASS** — sheet decode confirmed correct against Lumina 7.6.0
  (the materia chain correctly identified DoH/DoL melds; the session's one
  real finding was a missing hand/land guard, fixed in `9630391`).
- **Rotation smoke: PASS** — SAM rotation clean in live play.
- **Reload cycles: PASS (implicit)** — multiple Debug auto-reloads ran during
  the session with no unload hangs.
- **Occult canary: PASS** — zone HUD matched the in-game MKDInfo panel
  digit-exact in South Horn (knowledge level + progress, phantom job/level,
  silver/gold, potion/elixir/coffer counts) — covers the status-stack reads,
  AtkValue knowledge read, and currency item counts.
- Still pending: hunt-mode gold-icon targeting (low risk — generated property).

## Next validation step

Apply the `YalmDistanceX` → `CurrentDistance` swap when authorized (restores
the 18-warning baseline); Occult Duty tab check next time a toon is in South
Horn.
