using Daedalus.Data;

namespace Daedalus.Rotation.HecateCore.Helpers;

/// <summary>
/// Pure decisions about the Ley Lines circle, kept free of game services so they are unit-testable.
///
/// <para>
/// Ley Lines is ground-placed and its haste only applies while the player stands inside it. Drawing the
/// circle (status 737) and benefiting from it (status 738, Circle of Power) are therefore different
/// states, and any dodge separates them: the circle stays where it was drawn while the character is
/// steered out of it, and the remaining 20 seconds are wasted.
/// </para>
///
/// <para>
/// Retrace (Lv96) is the answer, and the cheap one — it re-places the circle at the player's feet and
/// explicitly does <b>not</b> reset the duration, so the only cost is its own 40s recast.
/// </para>
/// </summary>
public static class HecateLeyLineRules
{
    /// <summary>
    /// How much Ley Lines has to have left before Retrace is worth its 40s recast. Below this the
    /// circle is about to expire anyway and the recast would be spent for a couple of seconds of haste
    /// — and Retrace would then be unavailable for the next one.
    /// </summary>
    public const float RetraceMinRemainingSeconds = 5f;

    /// <summary>
    /// Whether Retrace should be used to bring the circle back to the player.
    /// <para>
    /// The defining condition is <paramref name="hasLeyLines"/> without
    /// <paramref name="inCircleOfPower"/>: a circle exists and the player is not in it. Standing in it
    /// already needs nothing; having no circle at all is Ley Lines' job, not Retrace's.
    /// </para>
    /// </summary>
    /// <param name="movementImminent">The timeline expects to move the player shortly — re-placing now would be undone.</param>
    public static bool ShouldRetrace(
        bool enabled,
        byte level,
        bool retraceReady,
        bool hasLeyLines,
        bool inCircleOfPower,
        float leyLinesRemaining,
        bool isMoving,
        bool movementImminent)
        => enabled
           && level >= BLMActions.Retrace.MinLevel
           && retraceReady
           && hasLeyLines
           && !inCircleOfPower
           && leyLinesRemaining >= RetraceMinRemainingSeconds
           && !isMoving
           && !movementImminent;
}
