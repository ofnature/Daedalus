# Impact Range: Dalamud 15.0.2.2 → 15.0.3.0

Reviewed 2026-07-28. Endpoint `15.0.3.0` was tagged the same day; post-tag
master activity is out of scope for this range.

| Item | Baseline | Endpoint |
| --- | --- | --- |
| Dalamud runtime | `15.0.2.2` / `4a6abae2` | `15.0.3.0` |
| FFXIVClientStructs | `82f00f3f` | `cc474ca9` |
| Lumina.Excel | `be40faf3` | `2b285467` |
| Lumina package | `7.5.0` | `7.6.0` |
| EXDSchema experimental | `61800c05` | `cf037c37` |
| SDK / packager | `15.0.0` / `15.0.0` | unchanged |
| API level | 15 | 15 |

## Files

- `Impact Matrix.md` — surface × plugin grid.
- `Daedalus.md`, `SealBreaker.md`, `Charon.md`, `Caduceus.md` — per-plugin
  impact with Before > After examples.

## One-paragraph verdict

No plugin needs a code migration: API 15 holds, SDK pins stay `15.0.0`, and the
only public API additions (`ConfigChangeEvent.Name`, `IFramework.CreateDebouncer`)
are additive with no local consumers. The exposure is native and lifecycle:
FFXIVClientStructs moved 226 commits across the full range (Character/GameObject/
Atk/PlayerState/ContentDirector all touched), the Item sheet schema changed,
Dalamud's shutdown/unload path was reworked, and the native hook backend gained
SafetyHook plus a Reloaded allocator bump. Priority order: **Charon**
(Character* casts + rider reads) → **Daedalus** (3 native hooks + widest CS
read surface + 18-warning baseline vs nullable-everywhere) → **SealBreaker**
(AddonLifecycle + manual Dalamud refs) → **Caduceus** (rebuild + smoke).
