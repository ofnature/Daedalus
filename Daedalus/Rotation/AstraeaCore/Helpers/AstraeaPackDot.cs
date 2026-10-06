namespace Daedalus.Rotation.AstraeaCore.Helpers;

/// <summary>
/// When Combust is worth a GCD in a pack that Gravity would hit instead.
/// <para>
/// One GCD of Combust is worth its tick potency for every 3 s the mob lives, up to 30 s. One GCD of Gravity
/// is its potency times the enemies it hits. Gravity is about twice a Combust tick at every rank (the game's
/// tooltips: Gravity 120 against Combust II's 60; the higher ranks keep the ratio), so Combust wins on a mob
/// that lives past about 6.5 s per enemy Gravity would hit: 20 s for three, 26 s for four, never for five.
/// Dot those, Gravity the rest — rather than RSR's all-or-nothing "Gravity at 3+, never dot".
/// </para>
/// </summary>
public static class AstraeaPackDot
{
    /// <summary>Gravity ÷ one Combust tick, times the 3 s tick: seconds of life per enemy hit.</summary>
    public const float SecondsPerEnemyHit = 6.5f;

    /// <summary>Every Combust rank lasts 30 s.</summary>
    public const float DotDurationSeconds = 30f;

    /// <summary>How long a mob must live for Combust to beat one more Gravity hitting <paramref name="hits"/>.</summary>
    public static float BreakEvenSeconds(int hits) => SecondsPerEnemyHit * hits;
}
