# Dalamud v15.0.2.2 to v15.0.2.3 - Public Report

Checked: `2026-07-28 06:08:53 -06:00`

This is a public-safe summary of the official Dalamud `15.0.2.2...15.0.2.3`, FFXIVClientStructs `82f00f3...0ce3f022`, Lumina.Excel `be40faf...011ca179`, and paired EXDSchema changes. It intentionally excludes private project names, local paths, and private implementation details.

## Sources

- Dalamud compare: `https://github.com/goatcorp/Dalamud/compare/15.0.2.2...15.0.2.3`
- Dalamud v15 page: `https://dalamud.dev/versions/v15/`
- Dalamud API reference: `https://dalamud.dev/api/`
- FFXIVClientStructs compare: `https://github.com/aers/FFXIVClientStructs/compare/82f00f3f1a1aa77219eda75d4ddaa29e66008684...0ce3f0220901a7c9f16d3fec526558e7829ca3b3`
- Lumina.Excel compare: `https://github.com/NotAdam/Lumina.Excel/compare/be40faf35c1ca040fe63a57d803072f10c60f6a9...011ca179aa022974a61078e132335ae46e4f4d90`
- EXDSchema compare: `https://github.com/xivdev/EXDSchema/compare/61800c05166fba97c411fd9c2aca764f42ad895a...99040ee2e4affb7c8dbd1d388707d83343c290b6`
- Dalamud SDK package index: `https://api.nuget.org/v3-flatcontainer/dalamud.net.sdk/index.json`
- Dalamud packager package index: `https://api.nuget.org/v3-flatcontainer/dalamudpackager/index.json`
- Referenced merged changes: [#2862](https://github.com/goatcorp/Dalamud/pull/2862), [#2866](https://github.com/goatcorp/Dalamud/pull/2866), [#2868](https://github.com/goatcorp/Dalamud/pull/2868), [#2870](https://github.com/goatcorp/Dalamud/pull/2870), [#2871](https://github.com/goatcorp/Dalamud/pull/2871), [#2872](https://github.com/goatcorp/Dalamud/pull/2872), [#2873](https://github.com/goatcorp/Dalamud/pull/2873), [#2876](https://github.com/goatcorp/Dalamud/pull/2876), [#2877](https://github.com/goatcorp/Dalamud/pull/2877), [#2880](https://github.com/goatcorp/Dalamud/pull/2880)

## Version Snapshot

| Item | Before | After |
| --- | --- | --- |
| Dalamud runtime | `15.0.2.2` / `4a6abae2` | `15.0.2.3` / `91ad60c8` |
| FFXIVClientStructs | `82f00f3f` | `0ce3f022` |
| Lumina.Excel | `be40faf3` | `011ca179` |
| Lumina package | `7.5.0` | `7.6.0` |
| Experimental EXDSchema | `61800c05` | `99040ee2` |
| Public SDK | `Dalamud.NET.Sdk/15.0.0` | unchanged |
| Public packager | `DalamudPackager/15.0.0` | unchanged |

## Summary

The official runtime moved from `15.0.2.2` to `15.0.2.3`.

- Dalamud range: 24 commits and 26 changed files.
- FFXIVClientStructs range: 88 commits and 133 changed files.
- Lumina.Excel range: 4 commits and 3 changed files.
- EXDSchema range: 6 commits and 7 changed schema files.
- Public plugin SDK remains `Dalamud.NET.Sdk/15.0.0`.
- Public packager remains `DalamudPackager/15.0.0`.

Do not switch a plugin project to a nonexistent `15.0.2.3` SDK. Keep:

```xml
<Project Sdk="Dalamud.NET.Sdk/15.0.0">
```

## Main Codebase Impacts

1. Config change events now expose the changed option's string `Name`.
2. Compatibility constructors and deconstruction remain for API 15 but are marked for later API 16 cleanup.
3. Copied-module SigScanner setup reads the `.text` section directly instead of retaining a full managed file copy.
4. SeString sheet-name validation is cached.
5. Invalid file drag-and-drop payloads clear stale path state.
6. Crash diagnostics now include BIOS information for additional hardware troubleshooting.
7. Dalamud.Boot adds an optional faster SqPack decompression fix using libdeflate.
8. Lumina moves to `7.6.0`, with seven paired schema files changed.
9. FFXIVClientStructs moves across native object, character, task, UI, agent, graphics, VFX, resource, and sound surfaces.

## Detailed Changes

### Config change events

`ConfigChangeEvent` and `ConfigChangeEvent<T>` now provide:

```csharp
public string Name { get; init; }
```

Runtime-created events populate both `Option` and `Name`. Existing API 15 code using the one-argument constructor or one-value deconstruction remains compatible.

The compatibility members are marked for API 16 cleanup. New code should prefer the current properties rather than relying on the old positional record shape.

### Copied SigScanner setup

Copied-module scanner initialization now reads the executable's `.text` section directly into the copied buffer. It no longer keeps a static byte array containing the entire module file.

This reduces managed-memory overhead for the copied-scanner path. The public `ISigScanner` method contract did not change, so this is a retest item rather than a signature-migration requirement.

### SeString evaluation

Dalamud now caches available Excel sheet names in its SeString evaluator. This reduces repeated sheet-name lookup work when resolving sheet-backed payloads.

No public SeString evaluator method was removed or renamed by this release.

### File drag-and-drop

An unrecognized drag payload now clears the prior file, directory, and extension collections. Consumers should no longer see paths retained from the last valid drag operation.

Plugins using drag-and-drop should still validate that the current payload is non-empty and acceptable before acting.

### Crash diagnostics

Crash logs now include BIOS information intended to help diagnose known instability on some Intel 13th- and 14th-generation systems.

This is a diagnostics improvement and does not create a plugin API requirement.

### Faster SqPack decompression

Dalamud.Boot adds an optional native `faster_decompression` fix backed by libdeflate, with supporting native dependency and build changes.

This is framework/bootstrap behavior. Plugins should not copy this hook or infer a change to normal Dalamud data APIs.

### Lumina and EXDSchema

Dalamud moves Lumina from `7.5.0` to `7.6.0`.

The paired schema update changes:

- `Adventure`;
- `Item`;
- `LegacyQuest`;
- `MKDGrowDataSJob`;
- `ModelChara`;
- `SatisfactionNpc`;
- `WKSScoreList`.

The changes cover sightseeing-log weather, item filter/repair metadata, journal genre links, model radius, and additional WKS score fields.

Plugins reading these sheets should rebuild and verify current generated field meaning.

### FFXIVClientStructs

The native dependency range is much larger than the Dalamud wrapper range: 88 commits and 133 files.

High-signal areas include:

- `GameMain` and `GameObjectManager` sizes;
- `GameObject` target-distance and target-status data;
- `Character`, `Companion`, and `Ornament`;
- `TaskManager`;
- `InfoProxySearch` and `InfoProxyContentMember`;
- `AgentContext`, lobby, and Mirage Prism agents;
- `AtkComponentRadioButton`, `AtkComponentTreeList`, and `AddonChatLog`;
- `AtkServer` and immediate graphics commands;
- graphics device, layout, VFX, and draw-object structures;
- sound controllers and sound data;
- many resource-handle types;
- generated native signatures and resolver data.

Direct ClientStructs users should rebuild against the target runtime bindings and retest every native feature they touch.

## Common Before > After Examples

### Config option name

Before:

```csharp
private void OnConfigChanged(ConfigChangeEvent change)
{
    HandleConfigChange(change.Option.ToString());
}
```

After:

```csharp
private void OnConfigChanged(ConfigChangeEvent change)
{
    var name = string.IsNullOrEmpty(change.Name)
        ? change.Option.ToString()
        : change.Name;

    HandleConfigChange(name);
}
```

The fallback preserves compatibility with events constructed through the retained API 15 constructor.

### SDK target

Incorrect:

```xml
<Project Sdk="Dalamud.NET.Sdk/15.0.2.3">
```

Correct:

```xml
<Project Sdk="Dalamud.NET.Sdk/15.0.0">
```

Runtime tags and public SDK package versions are separate.

### Native structures

Before:

```csharp
[StructLayout(LayoutKind.Explicit, Size = 0x1234)]
private struct CopiedNativeObject
{
}
```

After:

```csharp
var gameMain = GameMain.Instance();
if (gameMain == null)
    return;
```

Use current generated ClientStructs types and validate native pointers at the point of use. Avoid copied layouts, cached offsets, or fixed sizes unless the target runtime has been verified.

### Generated Excel rows

Before:

```csharp
var row = sheet.GetRow(rowId);
UseColumnByAssumedPosition(row);
```

After:

```csharp
if (!sheet.TryGetRow(rowId, out var row))
    return;

UseCurrentGeneratedFields(row);
```

Schema updates should be reviewed through current generated fields and explicit missing-row behavior.

### Drag-and-drop payloads

Before:

```csharp
if (lastFiles.Count > 0)
    Import(lastFiles);
```

After:

```csharp
if (currentFiles is { Count: > 0 })
    Import(currentFiles);
```

Treat drag payload data as current-operation state rather than assuming a previous valid list remains meaningful.

## Compatibility Assessment

| Plugin surface | Expected action |
| --- | --- |
| Managed plugin with no config-event, native, sheet, scanner, or drag/drop use | rebuild and normal smoke |
| `ConfigChangeEvent` consumer | review `Name`; old API 15 constructor/deconstruction still works |
| Standard `ISigScanner` consumer | no method migration; rescan and disposal smoke |
| Copied-module scanner consumer | memory behavior changed internally; repeat scan/load/unload smoke |
| Direct ClientStructs consumer | rebuild and feature-specific native smoke required |
| Consumer of one of the seven changed schemas | rebuild and validate generated fields/data |
| File drag-and-drop consumer | verify invalid and empty payload handling |

## Retest Checklist

- Rebuild against `Dalamud.NET.Sdk/15.0.0`.
- Confirm the distributed manifest still declares API level 15.
- Smoke config-event handlers and `Name` fallback behavior.
- Repeat signature scans and copied-scanner disposal.
- Smoke invalid, empty, and valid file drag-and-drop payloads.
- Smoke direct `GameMain`, `GameObjectManager`, character, target-status, task, agent, addon, graphics, VFX, sound, and resource-handle consumers.
- Smoke consumers of the seven changed Excel schemas.
- Test native-hook features through enable, disable, reload, logout, and shutdown.
- Treat a successful build as static evidence only; retain current-client runtime smoke as a separate gate.

## Post-Tag Boundary

This report stops at runtime tag `15.0.2.3` / `91ad60c8`.

Later master work—including shutdown changes, hook allocator movement, later ClientStructs/schema pins, nullable changes, and open PR `#2899`—is not part of the `15.0.2.3` runtime and must not be promoted into this release's migration requirements.

## Current Conclusion

Dalamud `15.0.2.3` is a runtime, native-binding, schema, performance, and diagnostics update. It is not a new plugin SDK release.

The clearest public API addition is `ConfigChangeEvent.Name`. The largest compatibility surface is the 88-commit FFXIVClientStructs update, followed by Lumina `7.6.0` and the seven changed schema files. Most managed plugins need only a rebuild and normal smoke; direct native and changed-sheet consumers need targeted runtime validation.
