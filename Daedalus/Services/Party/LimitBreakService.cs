using System;
using Dalamud.Plugin.Services;
using Daedalus.Services.Network;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Daedalus.Services.Party;

/// <summary>
/// Fires this toon's limit break when the fleet calls for its role.
/// <para>
/// The bar is shared and only one person can spend it, so the coordination window calls a ROLE
/// rather than a toon: every box hears the call, and the one whose job matches acts. Everyone
/// else drops it on the floor.
/// </para>
/// <para>
/// The cast is General Action 3 ("Limit Break") rather than a per-job action id. That is the same
/// button the hotbar uses, so the game resolves the role AND the tier for us — no table of
/// Braver/Bladedance/Final Heaven to keep current, and it cannot pick the wrong one.
/// </para>
/// </summary>
public sealed unsafe class LimitBreakService
{
    /// <summary>General Action 3 — "Limit Break". Resolves to the job's own LB at its current tier.</summary>
    private const uint LimitBreakGeneralAction = 3;

    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;

    private LimitBreakRole? _armedRole;
    private DateTime _armedUntilUtc = DateTime.MinValue;
    private DateTime _nextAttemptUtc = DateTime.MinValue;

    public LimitBreakService(IObjectTable objectTable, IPluginLog log)
    {
        _objectTable = objectTable;
        _log = log;
    }

    /// <summary>
    /// What happened to the last call this box heard. Shown in the coordination window — a limit
    /// break that silently does not go off is indistinguishable from one nobody called.
    /// </summary>
    public string LastOutcome { get; private set; } = "";

    /// <summary>True while a call is still being retried on this box.</summary>
    public bool IsArmed => _armedRole is not null && DateTime.UtcNow < _armedUntilUtc;

    /// <summary>
    /// A limit break was called for <paramref name="role"/>. Dropped immediately unless this
    /// toon's job answers for that role.
    /// </summary>
    public void Call(LimitBreakRole role)
    {
        var jobId = _objectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        if (!LimitBreakPolicy.Answers(role, jobId))
        {
            _armedRole = null;
            return;
        }

        _armedRole = role;
        _armedUntilUtc = DateTime.UtcNow.AddSeconds(LimitBreakPolicy.ArmWindowSeconds);
        _nextAttemptUtc = DateTime.MinValue;
        LastOutcome = $"{LimitBreakPolicy.Label(role)} LB called — trying";
    }

    /// <summary>Framework-thread pump. Retries the cast until it lands or the window lapses.</summary>
    public void Update()
    {
        if (_armedRole is not { } role)
            return;

        var now = DateTime.UtcNow;
        if (now >= _armedUntilUtc)
        {
            _armedRole = null;
            LastOutcome = $"{LimitBreakPolicy.Label(role)} LB not available — gave up";
            return;
        }

        if (now < _nextAttemptUtc)
            return;
        _nextAttemptUtc = now.AddSeconds(LimitBreakPolicy.RetryIntervalSeconds);

        if (_objectTable.LocalPlayer is null)
            return;

        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null)
                return;

            // Status covers everything we would otherwise have to re-derive badly: bar not full,
            // content that forbids LB, already casting, no valid target for a targeted LB.
            if (actionManager->GetActionStatus(ActionType.GeneralAction, LimitBreakGeneralAction) != 0)
                return;

            if (!actionManager->UseAction(ActionType.GeneralAction, LimitBreakGeneralAction))
                return;

            _armedRole = null;
            LastOutcome = $"{LimitBreakPolicy.Label(role)} LB fired";
            _log.Information("[LimitBreak] fired {Role} limit break", role);
        }
        catch (Exception ex)
        {
            _armedRole = null;
            LastOutcome = $"{LimitBreakPolicy.Label(role)} LB failed — {ex.GetType().Name}";
            _log.Warning(ex, "[LimitBreak] cast failed");
        }
    }
}
