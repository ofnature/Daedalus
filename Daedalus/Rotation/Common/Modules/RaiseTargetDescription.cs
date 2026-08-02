namespace Daedalus.Rotation.Common.Modules;

/// <summary>
/// Explains why a healer has no raise target.
/// </summary>
public static class RaiseTargetDescription
{
    /// <summary>
    /// Distinguishes an empty party from a body that is simply out of reach. The finder filters
    /// by spell range, so those two cases produced an identical "No target" — and nothing walks a
    /// healer toward a corpse, so in an open zone the second could hold for a whole fight while
    /// reading as though nobody needed raising.
    /// </summary>
    public static string Describe(
        float? nearestDeadDistance, float raiseRangeYalms, bool resurrectionBlocked = false)
    {
        // Checked first: a blocked corpse explains everything else, and reporting a range or
        // target problem over it would send the next reader somewhere useless.
        if (resurrectionBlocked)
            return "Resurrection blocked here — ordinary raises cannot land (Occult Raise only)";

        if (nearestDeadDistance is not { } distance)
            return "No target";

        return distance > raiseRangeYalms
            ? $"Dead ally {distance:F0}y away — out of {raiseRangeYalms:F0}y raise range"
            : "No target";
    }
}
