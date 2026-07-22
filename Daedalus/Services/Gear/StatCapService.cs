using System.Collections.Generic;
using Dalamud.Plugin.Services;
using Daedalus.Models.Gear;
using LuminaBaseParam = Lumina.Excel.Sheets.BaseParam;
using LuminaItemLevel = Lumina.Excel.Sheets.ItemLevel;

namespace Daedalus.Services.Gear;

/// <summary>
/// Per-piece per-stat caps from the game sheets: ItemLevel (per-ilvl stat base) × BaseParam
/// (per-equip-slot percentage) / 1000 — see <see cref="StatCapMath"/> for the formula and
/// provenance. Results cached per (ilvl, slot, twoHanded) since the sheets never change within
/// a session. Fail-open: missing sheet rows produce an empty cap set (UI shows "?", optimizer
/// skips the piece) rather than throwing mid-draw.
/// </summary>
public sealed class StatCapService
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    private readonly Dictionary<(int Ilvl, GearSlotId Slot, bool TwoHanded), IReadOnlyDictionary<uint, int>> _cache = new();

    /// <summary>Stats we compute caps for (the meldable substats + mains for display).</summary>
    private static readonly uint[] CapStats =
    {
        GearStatIds.CriticalHit, GearStatIds.Determination, GearStatIds.DirectHit,
        GearStatIds.SkillSpeed, GearStatIds.SpellSpeed, GearStatIds.Tenacity, GearStatIds.Piety,
    };

    public StatCapService(IDataManager dataManager, IPluginLog log)
    {
        _dataManager = dataManager;
        _log = log;
    }

    public IReadOnlyDictionary<uint, int> CapsFor(int ilvl, GearSlotId slot, bool twoHandedWeapon)
    {
        var key = (ilvl, slot, twoHandedWeapon);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var caps = Compute(ilvl, slot, twoHandedWeapon);
        _cache[key] = caps;
        return caps;
    }

    private IReadOnlyDictionary<uint, int> Compute(int ilvl, GearSlotId slot, bool twoHanded)
    {
        var result = new Dictionary<uint, int>();

        var ilvlSheet = _dataManager.GetExcelSheet<LuminaItemLevel>();
        var baseParamSheet = _dataManager.GetExcelSheet<LuminaBaseParam>();
        if (ilvlSheet == null || baseParamSheet == null)
            return result;

        var ilvlRow = ilvlSheet.GetRowOrDefault((uint)ilvl);
        if (ilvlRow == null)
            return result;

        foreach (var statId in CapStats)
        {
            var statBase = IlvlStatBase(ilvlRow.Value, statId);
            var paramRow = baseParamSheet.GetRowOrDefault(statId);
            if (paramRow == null)
                continue;

            var pct = SlotPercent(paramRow.Value, slot, twoHanded);
            var cap = StatCapMath.Cap(statBase, pct);
            if (cap > 0)
                result[statId] = cap;
        }

        return result;
    }

    /// <summary>ItemLevel sheet column for the stat (per-ilvl maximum base).</summary>
    private static int IlvlStatBase(LuminaItemLevel row, uint statId) => statId switch
    {
        GearStatIds.CriticalHit => row.CriticalHit,
        GearStatIds.Determination => row.Determination,
        GearStatIds.DirectHit => row.DirectHitRate,
        GearStatIds.SkillSpeed => row.SkillSpeed,
        GearStatIds.SpellSpeed => row.SpellSpeed,
        GearStatIds.Tenacity => row.Tenacity,
        GearStatIds.Piety => row.Piety,
        _ => 0,
    };

    /// <summary>BaseParam sheet per-slot percentage column (per-mille).</summary>
    private static int SlotPercent(LuminaBaseParam row, GearSlotId slot, bool twoHanded) => slot switch
    {
        GearSlotId.MainHand => twoHanded ? row.TwoHandWeaponPercent : row.OneHandWeaponPercent,
        GearSlotId.OffHand => row.OffHandPercent,
        GearSlotId.Head => row.HeadPercent,
        GearSlotId.Body => row.ChestPercent,
        GearSlotId.Hands => row.HandsPercent,
        GearSlotId.Legs => row.LegsPercent,
        GearSlotId.Feet => row.FeetPercent,
        GearSlotId.Ears => row.EarringPercent,
        GearSlotId.Neck => row.NecklacePercent,
        GearSlotId.Wrists => row.BraceletPercent,
        GearSlotId.RingL or GearSlotId.RingR => row.RingPercent,
        _ => 0,
    };
}
