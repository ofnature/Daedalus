namespace Daedalus.Services.Positional;

/// <summary>
/// Shared rules for when positional enforcement and vNav reposition apply.
/// Positionals only matter on single-target pulls; skip on multi-enemy packs.
/// </summary>
public static class PositionalRequirementHelper
{
    public const float MeleeRangeYalms = 5f;

    /// <summary>Scan radius when counting engaged hostiles for positional gating.</summary>
    public const float EngagedScanYalms = 25f;

    /// <summary>Skip positional enforce/vNav when more than one hostile is engaged in the pull.</summary>
    public static bool ShouldApply(int engagedEnemies)
        => engagedEnemies <= 1;
}
