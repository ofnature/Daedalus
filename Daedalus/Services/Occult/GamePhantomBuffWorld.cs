using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Services.Action;

namespace Daedalus.Services.Occult;

/// <summary>
/// Live game access for <see cref="PhantomBuffCycleService"/>. Everything native is read here so
/// the state machine stays testable; every read fails closed, because a bad read that looks like
/// "job switched" or "buff landed" would advance the cycle on a lie.
/// </summary>
public sealed class GamePhantomBuffWorld : IPhantomBuffWorld
{
    /// <summary>Knowledge Crystal EventObj, field-confirmed 2026-07-31 via the object labeller.</summary>
    private const uint KnowledgeCrystalBaseId = 2007457;

    /// <summary>Interaction range for the crystal — same order as the game's own object reach.</summary>
    private const float CrystalRangeYalms = 8f;

    private readonly PhantomJobService _phantomJobs;
    private readonly IActionService _actionService;
    private readonly IObjectTable _objectTable;
    private readonly ICondition _condition;
    private readonly Action<string>? _log;

    public GamePhantomBuffWorld(
        PhantomJobService phantomJobs,
        IActionService actionService,
        IObjectTable objectTable,
        ICondition condition,
        Action<string>? log = null)
    {
        _phantomJobs = phantomJobs;
        _actionService = actionService;
        _objectTable = objectTable;
        _condition = condition;
        _log = log;
    }

    public PhantomJob ActiveJob
    {
        get
        {
            try { return _phantomJobs.GetActiveJob().Job; }
            catch { return PhantomJob.None; }
        }
    }

    public IReadOnlyDictionary<PhantomJob, byte> JobLevels
    {
        get
        {
            try { return _phantomJobs.GetSnapshot().JobLevels; }
            catch { return new Dictionary<PhantomJob, byte>(); }
        }
    }

    public bool InOccultZone
    {
        get
        {
            try { return _phantomJobs.IsInOccultCrescent; }
            catch { return false; }
        }
    }

    public bool InCombat
    {
        get
        {
            try { return _condition[ConditionFlag.InCombat]; }
            catch { return true; } // fail closed: assume combat rather than start a cycle in one
        }
    }

    public bool NearKnowledgeCrystal
    {
        get
        {
            try
            {
                if (_objectTable.LocalPlayer is not { } player)
                    return false;

                foreach (var obj in _objectTable)
                {
                    if (obj.ObjectKind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventObj)
                        continue;
                    if (obj.BaseId != KnowledgeCrystalBaseId)
                        continue;
                    if (Vector3.Distance(player.Position, obj.Position) <= CrystalRangeYalms)
                        return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// <c>PublicContentOccultCrescent.ChangeSupportJob(byte)</c>, verified present at ClientStructs
    /// pin 8121cbbc. The byte is the MKDSupportJob RowId, which
    /// <see cref="PhantomJobData.GetSupportJobRowIndex"/> already derives (enum order matches the
    /// sheet, field-verified: Cannoneer = row 9) — reusing it rather than a second mapping keeps
    /// the read and write sides from ever disagreeing about what a job id means.
    /// </summary>
    public unsafe bool ChangeSupportJob(PhantomJob job)
    {
        try
        {
            var row = PhantomJobData.GetSupportJobRowIndex(job);
            if (row < 0 || row > byte.MaxValue)
                return false;

            return FFXIVClientStructs.FFXIV.Client.Game.InstanceContent
                .PublicContentOccultCrescent.ChangeSupportJob((byte)row);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"ChangeSupportJob({job}) threw: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The game's own verdict — <c>GetActionStatus == 0</c> — which covers level, learned,
    /// cooldown AND duty-bar slotting in one call. The duty bar is the one that actually bites:
    /// a phantom action not slotted on that job's bar simply cannot be used.
    /// </summary>
    public bool CanCast(uint actionId)
    {
        try { return _actionService.CanExecuteActionId(actionId); }
        catch { return false; }
    }

    public bool Cast(uint actionId, string actionName)
    {
        try
        {
            if (_objectTable.LocalPlayer is not { } player)
                return false;

            // Raw: no GetActionStatus pre-check (CanCast already asked) and no combat gate, which
            // is precisely why this path works out of combat where the scheduler does not run.
            var definition = _phantomJobs.GetOrBuildDefinition(actionId, actionName);
            return _actionService.ExecuteOgcdRaw(definition, actionId, player.GameObjectId);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"Buff cast {actionName} threw: {ex.Message}");
            return false;
        }
    }

    public float StatusRemaining(uint statusId)
    {
        try
        {
            if (_objectTable.LocalPlayer is not { } player || player.StatusList is null)
                return 0f;

            foreach (var status in player.StatusList)
            {
                if (status != null && status.StatusId == statusId)
                    return Math.Abs(status.RemainingTime);
            }

            return 0f;
        }
        catch
        {
            return 0f;
        }
    }
}
