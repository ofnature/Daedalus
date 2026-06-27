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
