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

    /// <summary>Last refusal code from GetActionStatus — reported so a silent "no" can be looked up.</summary>
    private uint _lastRefusalStatus;

    // A call this box is NOT the right job for. Tracked only so the line can eventually say
    // "nobody answered" instead of sitting on "waiting" forever, which is just a nicer-looking
    // version of the same silence.
    private LimitBreakRole? _waitingRole;
    private DateTime _waitingUntilUtc = DateTime.MinValue;

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
    /// Raised on the box that actually fires, so it can tell the rest of the fleet. Without this
    /// the operator's own window can only ever report its OWN toon's fate — which for a call it
    /// is not the right job for is "nothing", indistinguishable from the press not registering.
    /// </summary>
    public Action<LimitBreakRole>? OnFired { get; set; }

    /// <summary>
    /// A limit break was called for <paramref name="role"/>. Only a toon whose job answers for
    /// that role acts, but EVERY box records the call — a silent line on the box that pressed the
    /// button reads as a dead button (field 2026-08-11: pressed Melee, window kept saying
    /// "no call yet" while a different toon was the one meant to fire).
    /// </summary>
    public void Call(LimitBreakRole role)
    {
        var label = LimitBreakPolicy.Label(role);
        var jobId = _objectTable.LocalPlayer?.ClassJob.RowId ?? 0;
        if (!LimitBreakPolicy.Answers(role, jobId))
        {
            _armedRole = null;
            _waitingRole = role;
            _waitingUntilUtc = DateTime.UtcNow.AddSeconds(LimitBreakPolicy.AnswerWaitSeconds);
            LastOutcome = $"{label} LB called — waiting for a {label.ToLowerInvariant()}";
            return;
        }

        _armedRole = role;
        _waitingRole = null;
        _armedUntilUtc = DateTime.UtcNow.AddSeconds(LimitBreakPolicy.ArmWindowSeconds);
        _nextAttemptUtc = DateTime.MinValue;
        _lastRefusalStatus = 0;
        LastOutcome = $"{label} LB called — trying";
    }

    /// <summary>Another toon reported it fired. This is the only confirmation the operator gets.</summary>
    public void NoteRemoteFire(LimitBreakRole role, string characterName)
    {
        _armedRole = null;
        _waitingRole = null;
        LastOutcome = characterName.Length > 0
            ? $"{LimitBreakPolicy.Label(role)} LB fired by {characterName}"
            : $"{LimitBreakPolicy.Label(role)} LB fired";
    }

    /// <summary>Framework-thread pump. Retries the cast until it lands or the window lapses.</summary>
    public void Update()
    {
        var nowUtc = DateTime.UtcNow;

        // A call for somebody else that nobody ever confirmed. Say so rather than leaving the
        // line on "waiting", which is just a tidier-looking silence.
        if (_waitingRole is { } waiting && nowUtc >= _waitingUntilUtc)
        {
            _waitingRole = null;
            LastOutcome = $"{LimitBreakPolicy.Label(waiting)} LB — nobody answered";
        }

        if (_armedRole is not { } role)
            return;

        var now = nowUtc;
        if (now >= _armedUntilUtc)
        {
            _armedRole = null;

            // Name the refusal. "Not available" covers a full bar we mis-detected, content that
            // forbids limit breaks at all, and a targeted LB with nothing targeted — three very
            // different problems that a bare "gave up" cannot tell apart.
            LastOutcome = _lastRefusalStatus != 0
                ? $"{LimitBreakPolicy.Label(role)} LB refused by the game (status {_lastRefusalStatus})"
                : $"{LimitBreakPolicy.Label(role)} LB not available — gave up";
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
            var status = actionManager->GetActionStatus(ActionType.GeneralAction, LimitBreakGeneralAction);
            if (status != 0)
            {
                _lastRefusalStatus = status;
                return;
            }

            if (!actionManager->UseAction(ActionType.GeneralAction, LimitBreakGeneralAction))
                return;

            _armedRole = null;
            LastOutcome = $"{LimitBreakPolicy.Label(role)} LB fired";
            _log.Information("[LimitBreak] fired {Role} limit break", role);
            OnFired?.Invoke(role);
        }
        catch (Exception ex)
        {
            _armedRole = null;
            LastOutcome = $"{LimitBreakPolicy.Label(role)} LB failed — {ex.GetType().Name}";
            _log.Warning(ex, "[LimitBreak] cast failed");
        }
    }
}
