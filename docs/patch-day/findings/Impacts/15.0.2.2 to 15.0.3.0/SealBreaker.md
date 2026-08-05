# SealBreaker — Impact: Dalamud 15.0.2.2 → 15.0.3.0

- Source root (verified): `D:\Dev\SealBreaker` — project
  `SealBreaker\SealBreaker.csproj`.
- Baseline → endpoint: `15.0.2.2` → `15.0.3.0` (CS `82f00f3f` → `cc474ca9`).
- Metadata: plugin v1.1.0.9, **plain `Microsoft.NET.Sdk` with manual Dalamud
  references (NOT SDK-managed)**, manifest API level 15,
  `<Nullable>enable</Nullable>`.

## Scan counts and direct code surfaces

- `IAddonLifecycle` central registry in `Services/Service.cs` (lines 22–35):
  service injection, a `List<IAddonLifecycle.AddonEventDelegate>` handler
  registry, and a `Register(...)` helper — listener add/remove flows through
  one place.
- ClientStructs: `TargetSystem.Instance()`, `RecommendEquipModule.Instance()`,
  AtkValues/`AtkUnitBase` addon reads, `Utf8String`, `GameObject`;
  `PlayerState`-family reads in `Services/GrandCompanyState.cs`,
  `GcExchangeItemResolver.cs`, `GcShopCatalog.cs`, `FarmController.cs`.
- Lumina: `Item` sheet ×5 (GC turn-in valuation, Duckbone purchasing).
- Native hooks / SigScanner / ConfigChangeEvent / drag-drop: **none**.

## Current runtime-tag requirements

1. **Verify the manual Dalamud references first.** Because SealBreaker is not
   SDK-managed, nothing forces its `Dalamud.dll`/`FFXIVClientStructs.dll` hint
   paths onto the updated dev libs. Confirm the reference paths point at the
   live 15.0.3.0 hooks/dev folder, then rebuild.
2. `PlayerState`/`UIState` changed in the CS range — smoke GC state reads
   (rank, seals) against the in-game Grand Company window.
3. Item schema changed (Delta A) — run one expert-delivery turn-in and one
   Duckbone purchase; eyeball valuations.
4. `RecommendEquipModule` smoke: equip-recommend flow once.
5. Addon listener lifecycle: register → fire → unregister via the `Service.cs`
   registry across a plugin reload (shutdown rework made leaks visible).
6. `<Nullable>enable</Nullable>` + Dalamud's nullable-everywhere: expect
   possible new nullability warnings on rebuild; triage rather than suppress.

## Post-tag watch items

- `SpecialShop` schema changed in Delta B — GC shop catalog logic is
  struct/sheet adjacent; if Duckbone valuations ever drift, check this first.

## Impact assessment

**Medium.** Small native surface and no hooks, but the manual-reference build
is the fleet's only project that can silently compile against stale Dalamud
assemblies, which would mask every other risk in this report.

## Before > After Codebase Examples

### 1. Manual reference drift (`SealBreaker.csproj`)

Before (search-for pattern):

```xml
<Reference Include="Dalamud">
  <HintPath>C:\some\stale\copy\Dalamud.dll</HintPath>
</Reference>
```

After:

```xml
<Reference Include="Dalamud">
  <HintPath>$(AppData)\XIVLauncher\addon\Hooks\dev\Dalamud.dll</HintPath>
</Reference>
```

Upstream evidence: every binding change in this range (CS `cc474ca9`, Lumina
7.6.0) only reaches this plugin if the hint paths track the live dev folder.
If the csproj already uses the dev-folder pattern, **validation target**:
confirm the resolved assembly version after rebuild.

### 2. Addon handler registry unregistration (`Services/Service.cs:32-35`)

Before:

```csharp
public void Register(AddonEvent eventType, string addonName, IAddonLifecycle.AddonEventDelegate handler)
{
    AddonLifecycle.RegisterListener(eventType, addonName, handler);
    _handlers.Add(handler);
    // if nothing drains _handlers on dispose, listeners leak across reloads
}
```

After:

```csharp
public void Dispose()
{
    foreach (var h in _handlers)
        AddonLifecycle.UnregisterListener(h);
    _handlers.Clear();
}
```

Upstream evidence: shutdown/unload rework (#2857/#2896/#2897) — plugin unload
paths are stricter and leaked listeners double-fire after reload. If the
registry already unregisters on dispose, **validation target**: reload twice,
confirm single-fire.

### 3. Native singleton access (`TargetSystem`, `RecommendEquipModule`)

Before:

```csharp
var ts = TargetSystem.Instance();
var target = ts->Target; // no null gate
```

After:

```csharp
var ts = TargetSystem.Instance();
if (ts == null) return;
var target = ts->Target;
```

Upstream evidence: CS range touched `GameObject.cs` and UI modules; singleton
instances can be null during zone transitions (a documented crash pattern in
this fleet). If already guarded, **validation target** only.

## Current result

Reference paths verified 2026-07-28: the csproj prefers the `dev\` folder when
`Dalamud.dll` exists there, and dev holds 15.0.3.0 (no drift; the `15.0.2.0\`
fallback is unreachable while dev exists). Rebuilt Release: **0 errors,
0 warnings**.

## Next validation step

Check hint paths → rebuild → GC turn-in + Duckbone + equip-recommend smoke →
double-reload listener check.
