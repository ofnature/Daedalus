using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Daedalus.Services.Interaction;

/// <summary>
/// Clicks the object the boss engine says the fight needs clicked, once the toon is in the game's own interact range.
/// See <see cref="MechanicInteractPolicy"/> for when; this only gathers the facts and makes the call.
/// </summary>
public sealed unsafe class MechanicInteractService
{
    private readonly IObjectTable _objectTable;
    private readonly Func<bool> _enabled;
    private readonly Func<ulong> _targetId;
    private readonly IPluginLog? _log;

    private DateTime _lastAttempt = DateTime.MinValue;

    /// <summary>Called as the click goes out: asks the engine to stop uptime steering for a moment, since moving
    /// cancels most interactions. Danger still moves the toon.</summary>
    public Action<double>? RequestHold { get; set; }

    /// <summary>The last decision, for the debug readout.</summary>
    public MechanicInteractDecision LastDecision { get; private set; } = MechanicInteractDecision.NoTarget;

    public MechanicInteractService(IObjectTable objectTable, Func<bool> enabled, Func<ulong> targetId, IPluginLog? log = null)
    {
        _objectTable = objectTable;
        _enabled = enabled;
        _targetId = targetId;
        _log = log;
    }

    public void Update(IPlayerCharacter? player)
    {
        var enabled = _enabled();
        var id = enabled ? _targetId() : 0ul;
        var target = id != 0 ? _objectTable.SearchById(id) : null;
        var inRange = target != null && player != null
            && EventFramework.Instance() != null
            && EventFramework.Instance()->CheckInteractRange((GameObject*)player.Address, (GameObject*)target.Address, 1, false);
        var am = ActionManager.Instance();
        var now = DateTime.UtcNow;

        var situation = new MechanicInteractSituation(
            Enabled: enabled,
            TargetId: id,
            TargetFound: target != null,
            InRange: inRange,
            SelfAlive: player is { IsDead: false },
            SelfCasting: player?.IsCasting == true,
            AnimationLocked: am != null && am->AnimationLock > 0f,
            SecondsSinceLastAttempt: (now - _lastAttempt).TotalSeconds);

        LastDecision = MechanicInteractPolicy.Decide(situation);
        if (LastDecision != MechanicInteractDecision.Interact || target == null)
            return;

        var system = TargetSystem.Instance();
        if (system == null)
            return;

        RequestHold?.Invoke(MechanicInteractPolicy.RetrySeconds + 0.5);
        system->InteractWithObject((GameObject*)target.Address, false);
        _lastAttempt = now;
        _log?.Debug($"[Interact] {target.Name} ({id:X}) for the fight");
    }
}
