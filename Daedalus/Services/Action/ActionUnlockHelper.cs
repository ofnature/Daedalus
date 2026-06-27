using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using LuminaAction = Lumina.Excel.Sheets.Action;

namespace Daedalus.Services.Action;

/// <summary>
/// Job-quest / unlock-link gating for actions. Action-manager status 565 alone is not enough —
/// level-synced characters can report non-565 for quest-locked abilities until the class quest completes.
/// Mirrors RSR <c>ActionBasicInfo.IsQuestUnlocked</c>.
/// </summary>
public static unsafe class ActionUnlockHelper
{
    /// <summary>
    /// True when the action has no unlock link, or the link/quest is completed in UIState.
    /// </summary>
    public static bool IsActionQuestUnlocked(IDataManager? dataManager, uint actionId)
    {
        if (dataManager is null)
            return true;

        var row = dataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId);
        if (row is null)
            return true;

        return IsUnlockLinkSatisfied(row.Value.UnlockLink.RowId);
    }

    /// <summary>The shared global-GCD recast group (1-indexed). Actions whose primary cooldown group is the
    /// GCD store their own recast in <c>AdditionalCooldownGroup</c>.</summary>
    private const byte GcdCooldownGroup = 58;

    /// <summary>
    /// Resolves an action's own recast (cooldown) group from the Lumina Action sheet, RSR-style. Returns the
    /// 1-indexed group (caller passes group - 1 to <c>GetRecastGroupDetail</c>), or 0 if unavailable / the
    /// action only shares the GCD group. Used to read a cooldown via the recast GROUP when the by-action-id
    /// recast lookup fails (a ClientStructs quirk for some actions, e.g. GNB Bloodfest).
    /// </summary>
    public static byte GetCooldownGroup(IDataManager? dataManager, uint actionId)
    {
        if (dataManager is null)
            return 0;

        var row = dataManager.GetExcelSheet<LuminaAction>()?.GetRowOrDefault(actionId);
        if (row is null)
            return 0;

        var group = row.Value.CooldownGroup == GcdCooldownGroup
            ? row.Value.AdditionalCooldownGroup
            : row.Value.CooldownGroup;
        return group == GcdCooldownGroup ? (byte)0 : group;
    }

    /// <summary>
    /// Checks Lumina unlock link or quest id (&gt; 0x10000) via UIState.
    /// </summary>
    public static bool IsUnlockLinkSatisfied(uint unlockLinkOrQuestId)
    {
        if (unlockLinkOrQuestId == 0)
            return true;

        var uiState = UIState.Instance();
        if (uiState is null)
            return true;

        return uiState->IsUnlockLinkUnlockedOrQuestCompleted(unlockLinkOrQuestId);
    }
}
