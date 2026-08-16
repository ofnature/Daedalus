using System;
using Dalamud.Plugin.Services;
using Daedalus.Services.Network;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace Daedalus.Services.Party;

/// <summary>
/// Fires this toon's limit break when the fleet calls for its role.
/// <para>
/// The bar is shared and only one person can spend it, so the coordination window calls a ROLE
/// rather than a toon: every box hears the call, and the one whose job matches acts. Everyone
/// else drops it on the floor.
/// </para>
/// <para>
/// The concrete action is resolved from <c>LimitBreakController</c> — bar level, then
/// <c>GetActionId</c> for this character at that tier — so there is still no hand-kept table of
/// Braver/Bladedance/Final Heaven and the tier cannot be picked wrong. It is NOT fired as General
/// Action 3: that is the hotbar button and looks like the tidy answer, but BossMod Reborn rewrites
/// it into a real Spell before queueing anything, and BMR is the one that works in the field.
/// </para>
/// </summary>
public sealed unsafe class LimitBreakService
{
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;

    private LimitBreakRole? _armedRole;
    private DateTime _armedUntilUtc = DateTime.MinValue;
    private DateTime _nextAttemptUtc = DateTime.MinValue;

    /// <summary>Why the last attempt did not go out — reported so a silent "no" can be diagnosed.</summary>
    private string _lastRefusal = "";

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

    private string _lastOutcome = "";

    /// <summary>
    /// What happened to the last call this box heard. Shown in the coordination window — a limit
    /// break that silently does not go off is indistinguishable from one nobody called.
    /// <para>
    /// Mirrored into the Debug Log on every change, because the window line is transient and the
    /// operator is usually looking at the game rather than at it. Deduped, so the 250ms retry
    /// loop cannot flood the log.
    /// </para>
    /// </summary>
    public string LastOutcome
    {
        get => _lastOutcome;
        private set
        {
            if (string.Equals(_lastOutcome, value, StringComparison.Ordinal))
                return;

            _lastOutcome = value;
            if (value.Length == 0)
                return;

            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action,
                Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"Limit break: {value}");
        }
    }

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
        _lastRefusal = "";
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
            LastOutcome = _lastRefusal.Length > 0
                ? $"{LimitBreakPolicy.Label(role)} LB — {_lastRefusal}"
                : $"{LimitBreakPolicy.Label(role)} LB not available — gave up";
            return;
        }

        if (now < _nextAttemptUtc)
            return;
        _nextAttemptUtc = now.AddSeconds(LimitBreakPolicy.RetryIntervalSeconds);

        if (_objectTable.LocalPlayer is not { } player)
            return;

        try
        {
            var actionManager = ActionManager.Instance();
            if (actionManager == null)
                return;

            // Resolve the CONCRETE limit break rather than pressing General Action 3.
            //
            // General Action 3 is the hotbar's Limit Break button and it looked like the tidy
            // answer — the game picks the role and the tier for you. BossMod Reborn does not use
            // it that way, and BMR works in the field: ActionManagerEx.NormalizeActionForQueue
            // rewrites it into a real Spell before anything is queued, "for general actions, we
            // want to convert things we care about to spells". Same reason Sprint and the duty
            // actions are rewritten there. So we do the same conversion here.
            var lb = LimitBreakController.Instance();
            if (lb == null)
            {
                _lastRefusal = "no limit break in this content";
                return;
            }

            // Report the raw numbers on every failure. "Bar not charged" with nothing behind it
            // cannot be told apart from a bad struct read, and this feature has already been
            // diagnosed wrong twice from messages that stated a conclusion instead of evidence.
            var bar = $"units {lb->CurrentUnits}/{lb->BarUnits}";

            var level = lb->BarUnits != 0 ? lb->CurrentUnits / lb->BarUnits : 0;
            if (level == 0)
            {
                _lastRefusal = $"bar not charged ({bar})";
                return;
            }

            var actionId = lb->GetActionId((Character*)player.Address, (byte)(level - 1));
            if (actionId == 0)
            {
                _lastRefusal = $"no limit break for this job at bar {level} ({bar})";
                return;
            }

            // Status covers what is left: already casting, out of range, and — for the melee,
            // ranged and caster limit breaks, which are all targeted — nothing targeted.
            var status = actionManager->GetActionStatus(ActionType.Action, actionId);
            if (status != 0)
            {
                _lastRefusal = $"refused by the game (status {status}, action {actionId}, bar {level})";
                return;
            }

            if (!actionManager->UseAction(ActionType.Action, actionId))
            {
                _lastRefusal = $"cast rejected (action {actionId}, bar {level})";
                return;
            }

            _armedRole = null;
            LastOutcome = $"{LimitBreakPolicy.Label(role)} LB fired (bar {level})";
            _log.Information("[LimitBreak] fired {Role} limit break — action {Action}, bar {Level}",
                role, actionId, level);
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
