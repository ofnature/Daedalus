using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;

namespace Daedalus.Rotation.ProteusCore.Helpers;

/// <summary>
/// Status reads for the Blue Mage rotation. All status ids verified against the game sheets
/// (see <see cref="BLUActions.StatusIds"/>).
/// </summary>
public sealed class ProteusStatusHelper
{
    public bool HasMightyGuard(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.MightyGuard);

    public bool HasDiamondback(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.Diamondback);

    /// <summary>The Moon Flute +50% window is live.</summary>
    public bool HasWaxingNocturne(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.WaxingNocturne);

    /// <summary>Moon Flute hangover — 15s of total action lockout. Modules idle through it.</summary>
    public bool HasWaningNocturne(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.WaningNocturne);

    /// <summary>Bristle's +50% snapshot is armed — spend it on a DoT, never on filler.</summary>
    public bool HasBoost(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.Boost);

    /// <summary>Cold Fog converted — White Death is castable.</summary>
    public bool HasTouchOfFrost(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.TouchOfFrost);

    /// <summary>Mid-Surpanakha dump — the next press must also be Surpanakha.</summary>
    public bool HasSurpanakhasFury(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.SurpanakhasFury);

    public bool HasBasicInstinct(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.BasicInstinct);

    public bool HasToadOil(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.ToadOil);

    public bool HasGobskin(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.Gobskin);

    public bool HasHealerMimicry(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryHealer);

    /// <summary>Whether the mimicry matching the configured role is active.</summary>
    public bool HasMimicryForRole(IBattleChara player, BluRole role)
        => BaseStatusHelper.HasStatus(player, MimicryStatusFor(role));

    /// <summary>Whether ANY of the three mimicry buffs is active (wrong-role detection).</summary>
    public bool HasAnyMimicry(IBattleChara player)
        => BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryTank)
           || BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryDps)
           || BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryHealer);

    /// <summary>Which mimicry is active right now — "Tank"/"DPS"/"Healer" or "" for none.</summary>
    public string GetActiveMimicryName(IBattleChara player)
    {
        if (BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryTank)) return "Tank";
        if (BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryDps)) return "DPS";
        if (BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryHealer)) return "Healer";
        return "";
    }

    public static uint MimicryStatusFor(BluRole role) => role switch
    {
        BluRole.Tank => BLUActions.StatusIds.AethericMimicryTank,
        BluRole.Healer => BLUActions.StatusIds.AethericMimicryHealer,
        _ => BLUActions.StatusIds.AethericMimicryDps,
    };
}
