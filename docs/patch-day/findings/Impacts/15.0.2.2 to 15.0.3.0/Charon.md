# Charon — Impact: Dalamud 15.0.2.2 → 15.0.3.0

- Source root (verified): `D:\Dev\Charon` — project `Charon\Charon.csproj`.
- Baseline → endpoint: `15.0.2.2` → `15.0.3.0` (CS `82f00f3f` → `cc474ca9`).
- Metadata: plugin v0.1.15, `Dalamud.NET.Sdk/15.0.0`, manifest API level 15.

## Scan counts and direct code surfaces

- **Direct `Character*` pointer cast** in `Services/Game/GearManager.cs`
  (line ~1020) — reads native character data for gear handling.
- **`MountStateReader`** — reads rider `Character` structs for mount seat
  occupancy (its own comments note CS's MountContainer was stale once before).
- `IAddonLifecycle` listeners ×3 sites: `Services/Game/DutyPopInterop.cs`,
  `Services/Game/TeleportOfferInterop.cs`, `CharonPlugin.cs` (register +
  unregister paths).
- `InventoryItem.ItemFlags`, `GameObject` object-table scans,
  `Client.UI.Info`/`Misc` reads.
- Lumina: `Item` sheet ×4 (ExpBonusItems, gear).
- Native hooks / SigScanner / ConfigChangeEvent / drag-drop: **none**.

## Current runtime-tag requirements

1. Rebuild against 15.0.3.0 dev libs.
2. **Character-family smoke (highest priority in the fleet)**: the CS range
   changed `Character.cs`, `CharacterData.cs`, `DrawDataContainer.cs`, and
   `VfxContainer.cs` in Delta B — after they already changed in Delta A. Both
   the GearManager cast and every rider read sit on this. Smoke: mount, verify
   seat count and occupancy detection, run the auto-invite/pillion flow, then
   the gear feature.
3. Addon flow smoke: duty-pop and teleport-offer addons through register →
   fire → unregister (Atk surfaces churned underneath; no listener API change).
4. Sheet check: `Ornament` and `MountCustomize` schemas changed in Delta B —
   only relevant if mount metadata reads are added later; today's reads are
   struct-side.

## Post-tag watch items

- CS mount/companion/ornament areas are under active edit two ranges running —
  re-check `MountStateReader` assumptions every bump until it settles.

## Impact assessment

**High.** No code migration, but Charon is the only plugin doing raw
`Character*` casts and rider-struct walks, and that exact struct family changed
in both sub-ranges. A silent layout shift here corrupts reads instead of
crashing — smoke before trusting any pillion automation.

## Before > After Codebase Examples

### 1. Character cast validation (`Services/Game/GearManager.cs:~1020`)

Before (search-for pattern):

```csharp
var chara = (Character*)obj.Address; // assumes layout + kind
var value = chara->SomeField;
```

After:

```csharp
if (obj is not { ObjectKind: ObjectKind.Player } || obj.Address == nint.Zero)
    return;
var chara = (Character*)obj.Address;
// read only generated properties; no cached offsets
```

Upstream evidence: CS `Character.cs`/`CharacterData.cs` modified in
`0ce3f022...cc474ca9` (and earlier in `82f00f3...0ce3f022`). If Charon already
kind-checks and uses generated properties, this is a **validation target**:
rebuild and confirm gear values match the in-game window.

### 2. Rider/seat reads (`MountStateReader`)

Before:

```csharp
var mount = chara->Mount; // field shape assumed stable across bumps
```

After:

```csharp
// Read via current generated MountContainer members only; treat any
// unexpected zero/garbage seat data as "unknown" and fail the feature closed.
```

Upstream evidence: `DrawDataContainer.cs` + `VfxContainer.cs` changed in Delta
B; MountContainer staleness has bitten this exact reader before. Validation:
two-character mount test — seats, occupancy, dismount detection.

### 3. Addon listener lifecycle (`DutyPopInterop.cs`, `TeleportOfferInterop.cs`)

Before:

```csharp
_addonLifecycle.RegisterListener(AddonEvent.PostSetup, "ContentsFinderConfirm", OnPop);
// no unregister on dispose
```

After:

```csharp
public void Dispose()
    => _addonLifecycle.UnregisterListener(OnPop);
```

Upstream evidence: shutdown/unload rework (#2857/#2896/#2897) makes dispose
ordering stricter — leaked listeners now surface during the reworked unload.
If both interops already unregister, **validation target**: reload the plugin
twice and confirm no duplicate-listener double-fires.

## Current result

Rebuilt Release 2026-07-28 against the 15.0.3.0 dev libs: **0 errors,
0 warnings** — the `Character*` cast and rider reads compile clean against the
new generated bindings (static evidence only; layout-drift risk remains until
the mount/pillion smoke runs).

## Next validation step

Rebuild, then the Character-family smoke (mount/pillion/gear) before anything
else; then duty-pop/teleport-offer flows; then a double reload.
