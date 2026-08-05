# Dalamud 15.0.2.3 to 15.0.3.0 - Public Report

Checked: 2026-07-28

Public-safe summary of the official Dalamud `15.0.2.3...15.0.3.0` runtime
change, its FFXIVClientStructs/Lumina/EXDSchema movement, and general plugin
migration guidance. Extends the earlier `15.0.2.2...15.0.2.3` public report.
No private plugin names, paths, or implementation details are included.

## Sources

- Dalamud compare: `https://github.com/goatcorp/Dalamud/compare/15.0.2.3...15.0.3.0`
- FFXIVClientStructs compare: `https://github.com/aers/FFXIVClientStructs/compare/0ce3f0220901a7c9f16d3fec526558e7829ca3b3...cc474ca90dce0824334544ad7ec7d769f3cb6ee5`
- Lumina.Excel compare: `https://github.com/NotAdam/Lumina.Excel/compare/011ca179aa022974a61078e132335ae46e4f4d90...2b2854671facac83f02fdec1f355986f2edaeb3f`
- EXDSchema compare: `https://github.com/xivdev/EXDSchema/compare/99040ee2e4affb7c8dbd1d388707d83343c290b6...cf037c37eff351db4d1ca5952e10cc08c131b828`
- SDK/packager feeds: `https://api.nuget.org/v3-flatcontainer/dalamud.net.sdk/index.json`, `https://api.nuget.org/v3-flatcontainer/dalamudpackager/index.json`
- Referenced merged changes: #2857, #2867, #2878, #2879, #2885, #2886, #2888,
  #2889, #2890, #2891, #2892, #2893, #2894, #2896, #2897, #2898, #2899, #2900,
  #2901

## Version Snapshot

| Item | Before | After |
| --- | --- | --- |
| Dalamud runtime | `15.0.2.3` / `91ad60c8` | `15.0.3.0` |
| FFXIVClientStructs | `0ce3f022` | `cc474ca9` |
| Lumina.Excel | `011ca179` | `2b285467` |
| Lumina package | `7.6.0` | unchanged |
| EXDSchema experimental | `99040ee2` | `cf037c37` |
| Public SDK | `Dalamud.NET.Sdk/15.0.0` | unchanged |
| Public packager | `DalamudPackager/15.0.0` | unchanged |

Range size: 77 commits / 73 files (Dalamud); 138 commits / 113 files
(ClientStructs); 10 commits (Lumina.Excel, schema submodule only); 11 commits /
51 schema files (EXDSchema).

## Main Impacts

1. **Shutdown/unload rework.** Service unloading, texture release, font
   builds, and plugin disposal were restructured; the framework destroy hook
   is now global; manual Dalamud unload was fixed. Plugins should retest
   enable → disable → reload → game-exit cycles and make disposal idempotent
   with listeners/hooks unregistered before dependent services.
2. **Hook backend movement.** A safetyhook-based hook backend was added
   (designed to eventually replace Reloaded) and the Reloaded.Hooks dependency
   was bumped for allocator changes. The public hooking API did not change;
   native hook consumers should re-smoke hook creation, especially
   signature-resolved hooks.
3. **Nullable reference types enabled across all Dalamud projects** (was
   annotations-only). Rebuilds may surface new nullability warnings; triage
   rather than suppress.
4. **New additive API:** `IFramework.CreateDebouncer` / `IDebouncer`.
   `IAsyncDalamudPlugin.LoadAsync` now receives a cancellation token.
5. **UnlockState** updates are debounced and skip non-unlockable items; an
   Occult Crescent state null-guard was added — treat that native area as
   actively churning and null-check aggressively.
6. **Managed object wrappers** (`Character`, `GameObject` types) were touched
   alongside ClientStructs target-data changes — additive; rebuild covers it.
7. **ClientStructs:** broad movement across the Character family
   (Character/CharacterData/DrawData/Vfx), GameObject, GameMain,
   PlayerState/UIState, ContentDirector, FateManager, territory enums, agents,
   Atk components, graphics/file-system surfaces. Direct consumers should
   rebuild and retest every native feature they touch.
8. **Schemas:** the experimental EXDSchema pin advanced 51 sheets (including
   InstanceContent, SpecialShop, Ornament, MountCustomize, NotoriousMonster,
   and WKS sheets). `Item` did NOT change again in this range.

## Compatibility Assessment

| Plugin surface | Expected action |
| --- | --- |
| Managed-only plugin | rebuild and normal smoke, one reload cycle |
| Native hook consumer | rebuild; re-smoke each hook; verify signatures resolve |
| Addon-lifecycle listener consumer | verify unregister-on-dispose; double-reload test |
| Direct ClientStructs consumer | rebuild; feature-specific native smoke |
| Character/mount/gear struct reader | highest priority native smoke this range |
| Changed-schema consumer | rebuild and validate generated fields |
| Project with manual (non-SDK) Dalamud references | verify reference paths track the live dev libs before anything else |

Do not switch any plugin to a nonexistent `15.0.3.0` SDK; keep
`Dalamud.NET.Sdk/15.0.0`.

## Boundary

This report stops at runtime tag `15.0.3.0`, reviewed the day it was tagged.
Later master activity is not included and must not be promoted into this
range's requirements.
