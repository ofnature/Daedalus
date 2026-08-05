# Impact Matrix — 15.0.2.2 → 15.0.3.0

Legend: ● direct consumer (targeted smoke required) · ○ indirect/minor
(rebuild covers it) · — no consumer.

| Changed surface | Daedalus | SealBreaker | Charon | Caduceus |
| --- | --- | --- | --- | --- |
| `ConfigChangeEvent.Name` (new) | — | — | — | — |
| SigScanner internals (copied-module) | — | — | — | — |
| Drag-and-drop payload clearing | — | — | — | — |
| Native hooks (SafetyHook backend + Reloaded bump) | ● 3 hooks (DPS parser) | — | — | — |
| `IAddonLifecycle` listeners (Atk churn underneath) | — | ● registry in `Service.cs` | ● duty-pop + teleport-offer | — |
| Shutdown/unload rework (reload cycles) | ● (Debug auto-reload workflow) | ○ | ○ | ○ |
| Nullable-everywhere (warning baselines) | ● (18-warning hard baseline) | ○ (`<Nullable>enable</Nullable>`) | ○ | ○ |
| CS: `Character`/`CharacterData`/`DrawData`/`Vfx` | ○ | — | ● `Character*` cast + rider reads | — |
| CS: `GameObject` | ● NamePlateIconId, hitbox reads | ● TargetSystem targets | ● object scans | ○ |
| CS: `PlayerState` / `UIState` | ● gear/stats/farm services | ● GC state services | — | — |
| CS: `ContentDirector` (occult KL fallback) | ● | — | — | — |
| CS: `PublicContentOccultCrescent` + upstream null-guard | ● | — | — | — |
| CS: `AtkUnitBase`/AtkValues | ● MKDInfo read | ● addon reads | ● addon reads | — |
| CS: `TerritoryIntendedUse`/`TerritoryInfo` | ● | — | — | — |
| CS: `FateManager` | ○ (farm helpers) | — | — | — |
| Lumina `Item` sheet (Delta A schema change) | ● meld optimizer ×4 | ● turn-ins ×5 | ● gear/exp ×4 | — |
| EXDSchema Delta B (51 sheets; `SpecialShop`, `Ornament`, `MountCustomize`, `InstanceContent`…) | ○ | ○ (SpecialShop-adjacent GC shop) | ○ (mount sheets) | — |
| Texture/font shutdown changes | ○ 8 files | ○ 2 files | — | ○ 2 files |
| `LoadAsync` cancellation / async plugin | — (sync) | — (sync) | — (sync) | — (sync) |
| `UnlockState` debounce | — | — | — | — |

## Smoke priority

1. **Charon** — rebuild, then mount/pillion occupancy + gear manager + duty-pop
   and teleport-offer addon flows.
2. **Daedalus** — rebuild Debug+Release (watch the 18-warning baseline), then
   DPS parser hooks (damage lines + DoT ticks + screen-log), Occult Debug Duty
   tab in South Horn, melding tab vs Character window, hunt-mode targeting,
   several enable/disable/reload cycles.
3. **SealBreaker** — verify manual Dalamud lib refs resolve the 15.0.3.0 dev
   libs, rebuild, GC turn-in + Duckbone flows, addon listener register/unregister.
4. **Caduceus** — rebuild + normal smoke.
