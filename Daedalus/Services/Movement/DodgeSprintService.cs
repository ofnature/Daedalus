using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Daedalus.Services.Movement;

/// <summary>Everything the dodge-sprint decision needs, gathered by <see cref="DodgeSprintService"/>. Pure data so the
/// policy is unit-testable.</summary>
public readonly record struct DodgeSprintSituation(
    bool Enabled,
    bool Wanted,
    bool SelfAlive,
    bool Mounted,
    bool AlreadySprinting,
    bool SprintReady,
    bool AnimationLocked,
    double SecondsSinceLastAttempt);

/// <summary>Why Sprint was or was not pressed this frame; the first thing that would have to change.</summary>
public enum DodgeSprintDecision
{
    Sprint,
    Disabled,
    NotWanted,
    Dead,
    Mounted,
    AlreadySprinting,
    NotReady,
    Busy,
    Throttled,
}

/// <summary>
/// Sprinting out of a mechanic the dodge cannot walk out of in time. Minerva knows how far the way out is and when the
/// ground fires, and asks (<c>Minerva.Hints.WantSprint</c>); the button is ours. Eale's Arresting Gaze, 2026-10-03: the
/// way out of the cone was 18.4y with 2.9s left, walking covers 17.4y, and Rosa was paralysed a yalm and a half short;
/// she sprinted half a second before the hit. Sprint at the start covers about 22y.
/// </summary>
public static class DodgeSprintPolicy
{
    /// <summary>Between attempts: an action that did not go out is tried again a moment later, not every frame.</summary>
    public const double RetrySeconds = 0.5;

    public static DodgeSprintDecision Decide(in DodgeSprintSituation s)
        => !s.Enabled ? DodgeSprintDecision.Disabled
            : !s.Wanted ? DodgeSprintDecision.NotWanted
            : !s.SelfAlive ? DodgeSprintDecision.Dead
            : s.Mounted ? DodgeSprintDecision.Mounted
            : s.AlreadySprinting ? DodgeSprintDecision.AlreadySprinting
            : !s.SprintReady ? DodgeSprintDecision.NotReady
            : s.AnimationLocked ? DodgeSprintDecision.Busy
            : s.SecondsSinceLastAttempt < RetrySeconds ? DodgeSprintDecision.Throttled
            : DodgeSprintDecision.Sprint;
}

/// <summary>Presses Sprint when Minerva says the dodge needs it. See <see cref="DodgeSprintPolicy"/> for when.</summary>
public sealed unsafe class DodgeSprintService
{
    /// <summary>Sprint: action 3, status 50.</summary>
    private const uint SprintAction = 3, SprintStatus = 50;

    private readonly Func<bool> _enabled;
    private readonly Func<bool> _wanted;
    private readonly Func<bool> _mounted;
    private readonly IPluginLog? _log;
    private DateTime _lastAttempt = DateTime.MinValue;

    /// <summary>The last decision, for the debug readout.</summary>
    public DodgeSprintDecision LastDecision { get; private set; } = DodgeSprintDecision.NotWanted;

    public DodgeSprintService(Func<bool> enabled, Func<bool> wanted, Func<bool> mounted, IPluginLog? log = null)
    {
        _enabled = enabled;
        _wanted = wanted;
        _mounted = mounted;
        _log = log;
    }

    public void Update(IPlayerCharacter? player)
    {
        var enabled = _enabled();
        var wanted = enabled && _wanted();
        var am = ActionManager.Instance();
        var sprinting = false;
        if (player != null)
            foreach (var s in player.StatusList)
                if (s.StatusId == SprintStatus)
                    sprinting = true;
        var now = DateTime.UtcNow;

        var situation = new DodgeSprintSituation(
            Enabled: enabled,
            Wanted: wanted,
            SelfAlive: player is { IsDead: false },
            Mounted: _mounted(),
            AlreadySprinting: sprinting,
            SprintReady: am != null && am->GetActionStatus(ActionType.Action, SprintAction) == 0,
            AnimationLocked: am != null && am->AnimationLock > 0f,
            SecondsSinceLastAttempt: (now - _lastAttempt).TotalSeconds);

        LastDecision = DodgeSprintPolicy.Decide(situation);
        if (LastDecision != DodgeSprintDecision.Sprint)
            return;

        _lastAttempt = now;
        var sent = am->UseAction(ActionType.Action, SprintAction);
        _log?.Info("Dodge sprint: Minerva says the way out is longer than walking covers in time -- Sprint {0}.", sent ? "pressed" : "refused");
    }
}
