using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Daedalus.Services.Farm;

/// <summary>
/// Game reads and casts for farm mode's mounted travel — the impure half behind
/// <see cref="FarmMountPolicy"/>. Mount Roulette / specific mount casts, dismount, unlock probes,
/// and zone flight legality. All fail-open: a failed probe reports "can't" and the farm walks.
/// </summary>
public interface IFarmMountHelper
{
    bool IsMounted { get; }
    bool IsInFlight { get; }
    bool IsInCombat { get; }

    /// <summary>True when the specific mount row is unlocked on this toon.</summary>
    bool IsMountUnlocked(uint mountId);

    /// <summary>
    /// Casts the mount (specific when <paramref name="useSpecific"/>, else Mount Roulette).
    /// Pre-gates on GetActionStatus — mounted-state errors are silent otherwise.
    /// </summary>
    bool TryMount(bool useSpecific, uint specificMountId, out string detail);

    /// <summary>Casts Dismount (General Action 23). While airborne this starts the descent.</summary>
    bool TryDismount();

    /// <summary>
    /// True when the current zone supports flying AND this toon has attuned its aether currents.
    /// Unknown (probe failure) reports the zone's flight support alone — the vNav fly-path
    /// fallback in the caller covers a wrong "yes".
    /// </summary>
    bool CanFlyInCurrentZone();

    /// <summary>Unlocked mounts (id + display name) for the picker UI. Not cached — cache at the call site.</summary>
    IReadOnlyList<(uint Id, string Name)> GetUnlockedMounts();
}

public sealed unsafe class FarmMountHelper : IFarmMountHelper
{
    /// <summary>General Action 9 — Mount Roulette.</summary>
    private const uint MountRouletteGeneralAction = 9;

    /// <summary>General Action 23 — Dismount.</summary>
    private const uint DismountGeneralAction = 23;

    private readonly ICondition _condition;
    private readonly IDataManager _dataManager;
    private readonly IClientState _clientState;
    private readonly IPluginLog _log;

    public FarmMountHelper(
        ICondition condition,
        IDataManager dataManager,
        IClientState clientState,
        IPluginLog log)
    {
        _condition = condition;
        _dataManager = dataManager;
        _clientState = clientState;
        _log = log;
    }

    public bool IsMounted => _condition[ConditionFlag.Mounted];

    public bool IsInFlight => _condition[ConditionFlag.InFlight];

    public bool IsInCombat => _condition[ConditionFlag.InCombat];

    public bool IsMountUnlocked(uint mountId)
    {
        if (mountId == 0)
            return false;

        try
        {
            var playerState = PlayerState.Instance();
            return playerState != null && playerState->IsMountUnlocked(mountId);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[FarmMount] IsMountUnlocked probe failed");
            return false;
        }
    }

    public bool TryMount(bool useSpecific, uint specificMountId, out string detail)
    {
        detail = "";
        if (IsMounted)
        {
            detail = "already mounted";
            return false;
        }

        if (IsInCombat)
        {
            // The decision-to-cast gap gotcha: combat can start between the policy tick and this
            // call — re-check at cast time, never trust the earlier evaluation alone.
            detail = "in combat";
            return false;
        }

        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null)
            {
                detail = "ActionManager unavailable";
                return false;
            }

            var type = useSpecific ? ActionType.Mount : ActionType.GeneralAction;
            var id = useSpecific ? specificMountId : MountRouletteGeneralAction;

            var status = actionManager->GetActionStatus(type, id);
            if (status != 0)
            {
                detail = $"action status {status}";
                return false;
            }

            if (!actionManager->UseAction(type, id))
            {
                detail = "UseAction returned false";
                return false;
            }

            detail = useSpecific ? $"mount {specificMountId}" : "roulette";
            return true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[FarmMount] mount cast failed");
            detail = ex.GetType().Name;
            return false;
        }
    }

    public bool TryDismount()
    {
        if (!IsMounted)
            return true;

        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null)
                return false;

            if (actionManager->GetActionStatus(ActionType.GeneralAction, DismountGeneralAction) != 0)
                return false;

            return actionManager->UseAction(ActionType.GeneralAction, DismountGeneralAction);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "[FarmMount] dismount failed");
            return false;
        }
    }

    public bool CanFlyInCurrentZone()
    {
        try
        {
            var territoryRow = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?
                .GetRowOrDefault(_clientState.TerritoryType);
            if (territoryRow == null)
                return false;

            // No aether-current set = the zone has no flight at all (most ARR overworld).
            var compFlgSetId = territoryRow.Value.AetherCurrentCompFlgSet.RowId;
            if (compFlgSetId == 0)
                return false;

            var playerState = PlayerState.Instance();
            if (playerState == null)
                return true; // zone supports flight; attunement unknown — caller's fly fallback covers it

            return playerState->IsAetherCurrentZoneComplete(compFlgSetId);
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[FarmMount] flight probe failed");
            return false;
        }
    }

    public IReadOnlyList<(uint Id, string Name)> GetUnlockedMounts()
    {
        var result = new List<(uint, string)>();
        try
        {
            var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Mount>();
            if (sheet == null)
                return result;

            foreach (var row in sheet)
            {
                if (row.RowId == 0 || row.Order <= 0)
                    continue;

                var name = row.Singular.ExtractText();
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (!IsMountUnlocked(row.RowId))
                    continue;

                // Sheet names are lowercase singular ("company chocobo") — title-case the first letter.
                if (name.Length > 0 && char.IsLower(name[0]))
                    name = char.ToUpperInvariant(name[0]) + name[1..];

                result.Add((row.RowId, name));
            }

            result.Sort((a, b) => string.CompareOrdinal(a.Item2, b.Item2));
        }
        catch (Exception ex)
        {
            _log.Debug(ex, "[FarmMount] mount enumeration failed");
        }

        return result;
    }
}
