using System;
using System.Collections.Generic;
using Daedalus.Services;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// Enemies that resisted one debuff-only action, so it is never cast at them again.
/// <para>
/// A debuff action is paced on "the target doesn't have the debuff yet", and an enemy that resists or
/// is immune never gets it — so without this the gate stays open and the cast repeats for as long as
/// the enemy lives. The game's own answer comes back in the action-effect packet
/// (<see cref="LocalActionOutcome.StatusResisted"/> / <see cref="LocalActionOutcome.NoEffect"/>).
/// </para>
/// </summary>
public sealed class ResistedDebuffMemory
{
    private readonly uint _actionId;
    private readonly HashSet<uint> _resisted = [];

    public ResistedDebuffMemory(uint actionId) => _actionId = actionId;

    public int Count => _resisted.Count;

    /// <summary>Feed every local action outcome; only this action's resists are kept.</summary>
    public void Observe(uint actionId, uint targetEntityId, LocalActionOutcome outcome)
    {
        if (actionId == _actionId
            && outcome is LocalActionOutcome.StatusResisted or LocalActionOutcome.NoEffect)
            _resisted.Add(targetEntityId);
    }

    public bool HasResisted(uint targetEntityId) => _resisted.Contains(targetEntityId);

    /// <summary>Forget enemies that are gone, so a reused entity id starts fresh.</summary>
    public void Prune(Func<uint, bool> stillAlive)
        => _resisted.RemoveWhere(id => !stillAlive(id));
}
