using System.Collections.Generic;
using Daedalus.Rotation.AstraeaCore.Context;
using Daedalus.Rotation.AstraeaCore.Helpers;
using Daedalus.Rotation.AstraeaCore.Modules.Healing;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.AstraeaCore.Modules;

/// <summary>
/// Coordinates healing for Astrologian. All 17 healing handlers are now scheduler-driven —
/// EarthlyStarPlacement uses PushGroundTargetedOgcd, the rest push regular oGCD/GCD candidates.
/// </summary>
public sealed class HealingModule : IAstraeaModule
{
    private readonly List<IHealingHandler> _handlers;

    public int Priority => 10;
    public string Name => "Healing";

    public HealingModule()
    {
        _handlers = new List<IHealingHandler>
        {
            new DoomTopOffHandler(),
            new PreemptiveHealingHandler(),
            new EsunaHandler(),
            new EssentialDignityHandler(),
            new CelestialIntersectionHandler(),
            new CelestialOppositionHandler(),
            new ExaltationHandler(),
            new HoroscopeDetonationHandler(),
            new MicrocosmosHandler(),
            new EarthlyStarDetonationHandler(),
            new SynastryHandler(),
            new EarthlyStarPlacementHandler(),
            new LadyOfCrownsHandler(),
            new HoroscopePreparationHandler(),
            new MacrocosmosHandler(),
            new AoEHealingHandler(),
            new AspectedBeneficHandler(),
            new SingleTargetHandler(),
        };
    }

    public bool TryExecute(IAstraeaContext context, bool isMoving) => false;

    public void CollectCandidates(IAstraeaContext context, RotationScheduler scheduler, bool isMoving)
    {
        context.HealingCoordination.Clear();
        if (!context.InCombat) return;
        if (!context.Configuration.EnableHealing) return;
        if (AstraeaCardHelper.HasAstlock(context)) return;

        // The lockout holds only the routine GCD heals. Everything else still runs — above all the
        // handlers that SPEND the lockout states (Stellar Detonation for a mature star, Microcosmos
        // for Macrocosmos): returning before them, as this used to, held the very release the
        // lockout waits for, and with it every heal. RSR's equivalent (MicroPrio) presses
        // Detonation and Microcosmos before its hold, and holds nothing else.
        var lockout = AstraeaCardHelper.HasHealingLockout(context);
        if (lockout)
            context.Debug.PlanningState = "Healing lockout (Macrocosmos): GCD heals held";

        foreach (var handler in _handlers)
        {
            if (!RunsUnder(handler, lockout))
                continue;
            handler.CollectCandidates(context, scheduler, isMoving);
        }
    }

    /// <summary>The GCD heals the lockout holds; the detonations and oGCD heals are never held.</summary>
    internal static bool IsRoutineGcdHeal(IHealingHandler handler) =>
        handler is AspectedBeneficHandler or SingleTargetHandler or AoEHealingHandler;

    /// <summary>
    /// Whether this handler runs this frame, given the lockout state. Pure, for tests.
    /// </summary>
    internal static bool RunsUnder(IHealingHandler handler, bool lockout) =>
        !lockout || !IsRoutineGcdHeal(handler);



    public void UpdateDebugState(IAstraeaContext context)
    {
        var (avgHp, lowestHp, injured) = context.PartyHealthMetrics;
        context.Debug.AoEInjuredCount = injured;
        context.Debug.PlayerHpPercent = context.Player.MaxHp > 0
            ? (float)context.Player.CurrentHp / context.Player.MaxHp
            : 1f;
    }
}
