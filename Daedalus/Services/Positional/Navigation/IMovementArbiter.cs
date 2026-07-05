namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Single gate in front of vNav: every Daedalus movement intent flows through this so (a) two services
/// can never fight over the path in the same frame and (b) Daedalus yields the input pipeline to
/// BossMod whenever BMR is dodging or danger is live/imminent (BMR steers by input injection and defers
/// to vNav while a path runs — without this gate the two systems tug-of-war and stutter the screen).
/// </summary>
public interface IMovementArbiter : IVNavService
{
    /// <summary>
    /// Once per framework tick, before rotation execution: sample BMR/vNav state and run the
    /// yield + cooldown state machine (stops a Daedalus-owned path when danger appears).
    /// </summary>
    void BeginFrame();

    /// <summary>
    /// BMR AI is actively steering the character (has a nav target). Input-injection movement is invisible
    /// to <see cref="IVNavService.IsPathRunning"/>, so casters should also hold hard-casts on this.
    /// Deliberately narrower than the vNav yield gate: danger-imminent without a nav target (e.g. an
    /// unavoidable raidwide) does not move the character and must not block casting.
    /// </summary>
    bool IsExternalMovementActive { get; }

    /// <summary>State of the last <see cref="BeginFrame"/> / submission, for UI.</summary>
    MovementArbiterSnapshot Snapshot { get; }

    /// <summary>
    /// Intent-typed submission. <see cref="MovementIntent.PositionalArc"/> bypasses the steering-only
    /// yield (BMR keeps range, Daedalus owns the angle) but never real zone/damage danger. The plain
    /// <see cref="IVNavService"/> methods route to <see cref="MovementIntent.MaxMelee"/>.
    /// </summary>
    VNavMoveResult PathfindAndMoveCloseTo(System.Numerics.Vector3 destination, float toleranceYalms, MovementIntent intent, bool fly = false);
}
