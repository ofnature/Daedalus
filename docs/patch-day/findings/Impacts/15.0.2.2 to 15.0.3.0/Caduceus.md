# Caduceus — Impact: Dalamud 15.0.2.2 → 15.0.3.0

- Source root (verified): `D:\Dev\Caduceus` — project
  `Caduceus\Caduceus.csproj`.
- Baseline → endpoint: `15.0.2.2` → `15.0.3.0`.
- Metadata: plugin v0.1.0, `Dalamud.NET.Sdk/15.0.0`, manifest API level 15.

## Scan counts and direct code surfaces

- Minimal ClientStructs surface: light `Client.Game` and `Client.UI.Misc`
  usage only.
- Texture/font surfaces in 2 files (`ITextureProvider`/FontAtlas).
- No native hooks, no SigScanner, no ConfigChangeEvent, no drag-drop, no
  Lumina reads of any changed sheet (zero `Item` refs), no addon-lifecycle
  listeners, sync plugin entry.

## Current runtime-tag requirements

1. Rebuild against the 15.0.3.0 dev libs.
2. Normal smoke: open windows, exercise the mouseover-heal targeting path once.
3. One reload cycle (shutdown/unload rework) — confirm clean unload in the log.

## Post-tag watch items

None specific. Caduceus matches the report's "managed plugin, rebuild only"
compatibility row.

## Impact assessment

**Low.** Nothing in either delta maps onto a direct consumer here.

## Before > After Codebase Examples

### 1. Reload-safe disposal (plugin entry)

Before (search-for pattern):

```csharp
public void Dispose()
{
    // windows removed but shared textures/fonts left to the framework
}
```

After:

```csharp
public void Dispose()
{
    windowSystem.RemoveAllWindows();
    texture?.Dispose(); // shutdown rework cancels texture/font work in flight
}
```

Upstream evidence: texture release + font-build cancellation on shutdown
(#2896 and related). If disposal already covers owned resources, **validation
target**: one reload + one game exit with no texture errors logged.

## Current result

Rebuilt Release 2026-07-28 against the 15.0.3.0 dev libs: **0 errors,
0 warnings**.

## Next validation step

Rebuild + the three-step smoke above.
