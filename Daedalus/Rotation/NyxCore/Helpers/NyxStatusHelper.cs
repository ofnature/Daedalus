using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;

namespace Daedalus.Rotation.NyxCore.Helpers;

/// <summary>
/// Helper class for checking Dark Knight status effects.
/// </summary>
public sealed class NyxStatusHelper : BaseStatusHelper
{
    #region Tank Stance

    /// <summary>
    /// Checks if the player has Grit (tank stance) active.
    /// </summary>
    public bool HasGrit(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.Grit);
    }

    #endregion

    #region Darkside and Dark Arts

    /// <summary>
    /// Checks if the player has Dark Arts active (TBN broke - free Edge/Flood).
    /// </summary>
    public bool HasDarkArts(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.DarkArts);
    }

    #endregion

    #region Damage Buffs

    /// <summary>
    /// Checks if the player has Blood Weapon active.
    /// </summary>
    public bool HasBloodWeapon(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.BloodWeapon);
    }

    /// <summary>
    /// Gets the remaining duration of Blood Weapon.
    /// </summary>
    public float GetBloodWeaponRemaining(IBattleChara player)
    {
        return GetStatusRemaining(player, DRKActions.StatusIds.BloodWeapon);
    }

    /// <summary>
    /// Checks if the player has Delirium active.
    /// </summary>
    public bool HasDelirium(IBattleChara player)
    {
        // Lv.96+ Delirium applies status 3836 (enables the Scarlet Delirium combo); pre-96 applies 1972.
        // Checking only one ID is the known single-status bug — at 96+ the combo never triggered.
        return HasStatus(player, DRKActions.StatusIds.Delirium)
            || HasStatus(player, DRKActions.StatusIds.Delirium96);
    }

    /// <summary>
    /// Gets the number of Delirium stacks remaining (3836 at Lv.96+, 1972 pre-96).
    /// </summary>
    public int GetDeliriumStacks(IBattleChara player)
    {
        var stacks96 = GetStatusStacks(player, DRKActions.StatusIds.Delirium96);
        return stacks96 > 0 ? stacks96 : GetStatusStacks(player, DRKActions.StatusIds.Delirium);
    }

    /// <summary>
    /// Checks if Scornful Edge buff is active (from Torcleaver at Lv.96+).
    /// Enables Disesteem.
    /// </summary>
    public bool HasScornfulEdge(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.ScornfulEdge);
    }

    #endregion

    #region Living Shadow

    /// <summary>
    /// Checks if Living Shadow is currently active.
    /// Note: Living Shadow doesn't apply a visible status to the player,
    /// so this checks for the presence of the summoned entity.
    /// For now, we'll track via action cooldown in the rotation.
    /// </summary>
    public bool HasLivingShadow(IBattleChara player)
    {
        // Living Shadow doesn't give the player a status buff
        // We track this via the action's cooldown state instead
        // Return false here - actual tracking done via action cooldown
        return false;
    }

    #endregion

    #region Ground DoT

    /// <summary>
    /// Checks if Salted Earth is active. Salted Earth applies a player status (749) for its 15s duration —
    /// RSR tracks it via PlayerHasStatus(SaltedEarth). The old "no player buff" assumption was wrong and
    /// left Salt and Darkness (which requires Salted Earth active) permanently unable to fire.
    /// </summary>
    public bool HasSaltedEarth(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.SaltedEarth);
    }

    #endregion

    #region Defensive Buffs

    /// <summary>
    /// Checks if the player has Living Dead active.
    /// </summary>
    public bool HasLivingDead(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.LivingDead);
    }

    /// <summary>
    /// Checks if the player has Walking Dead active (MUST be healed to full or die).
    /// </summary>
    public bool HasWalkingDead(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.WalkingDead);
    }

    /// <summary>
    /// Checks if the player has Shadow Wall/Shadowed Vigil active.
    /// </summary>
    public bool HasShadowWall(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.ShadowWall) ||
               HasStatus(player, DRKActions.StatusIds.ShadowedVigil);
    }

    /// <summary>
    /// Checks if the player has Dark Mind active.
    /// </summary>
    public bool HasDarkMind(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.DarkMind);
    }

    /// <summary>
    /// Checks if the player has Dark Missionary active.
    /// </summary>
    public bool HasDarkMissionary(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.DarkMissionary);
    }

    /// <summary>
    /// Checks if the player has The Blackest Night shield active.
    /// </summary>
    public bool HasTheBlackestNight(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.TheBlackestNight);
    }

    /// <summary>
    /// Checks if the player has Oblation active.
    /// </summary>
    public bool HasOblation(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.Oblation);
    }

    /// <summary>
    /// Checks if the player has Rampart active.
    /// </summary>
    public bool HasRampart(IBattleChara player)
    {
        return HasStatus(player, DRKActions.StatusIds.Rampart);
    }

    /// <summary>
    /// Checks if any defensive cooldown is active.
    /// </summary>
    public bool HasActiveMitigation(IBattleChara player)
    {
        return HasLivingDead(player) ||
               HasWalkingDead(player) ||
               HasShadowWall(player) ||
               HasDarkMind(player) ||
               HasTheBlackestNight(player) ||
               HasOblation(player) ||
               HasRampart(player) ||
               HasStatus(player, DRKActions.StatusIds.ArmsLength);
    }

    /// <summary>
    /// Gets a string listing all active mitigations for debug display.
    /// </summary>
    public string GetActiveMitigations(IBattleChara player)
    {
        var active = new System.Collections.Generic.List<string>();

        if (HasLivingDead(player)) active.Add("Living Dead");
        if (HasWalkingDead(player)) active.Add("Walking Dead!");
        if (HasShadowWall(player)) active.Add("Shadow Wall");
        if (HasDarkMind(player)) active.Add("Dark Mind");
        if (HasTheBlackestNight(player)) active.Add("TBN");
        if (HasOblation(player)) active.Add("Oblation");
        if (HasRampart(player)) active.Add("Rampart");
        if (HasStatus(player, DRKActions.StatusIds.ArmsLength)) active.Add("Arm's Length");

        return FormatActiveList(active);
    }

    #endregion

    // Core status methods (HasStatus, GetStatusRemaining, GetStatusStacks) inherited from BaseStatusHelper
}
