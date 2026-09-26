using System.Collections.Generic;
using Daedalus.Rotation.ApolloCore.Context;
using Daedalus.Rotation.ApolloCore.Modules.Healing;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.ApolloCore.Modules;

/// <summary>
/// Orchestrates healing sub-handlers for the WHM rotation.
/// Each handler pushes its own scheduler candidates; dispatch happens centrally.
/// </summary>
public sealed class HealingModule : IApolloModule
{
    private readonly List<IHealingHandler> _handlers;

    public int Priority => 10;
    public string Name => "Healing";

    public HealingModule()
    {
        _handlers = new List<IHealingHandler>
        {
            new DoomTopOffHandler(),
            new BenedictionHandler(),
            new AssizeHealingHandler(),
            new TetragrammatonHandler(),
            new EsunaHandler(),
            new PreemptiveHealingHandler(),
            new RegenHandler(),
            new AoEHealingHandler(),
            new SingleTargetHealingHandler(),
            new BloodLilyBuildingHandler(),
            new LilyCapPreventionHandler(),
        };
    }

    public bool TryExecute(IApolloContext context, bool isMoving) => false;

    public void CollectCandidates(IApolloContext context, RotationScheduler scheduler, bool isMoving)
    {
        context.HealingCoordination.Clear();
        if (!context.InCombat) return;

        foreach (var handler in _handlers)
            handler.CollectCandidates(context, scheduler, isMoving);
    }

    public void UpdateDebugState(IApolloContext context)
    {
        // Debug state is updated during handler execution.
    }
}
