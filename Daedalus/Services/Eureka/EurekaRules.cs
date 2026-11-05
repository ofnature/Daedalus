using System;
using Daedalus.Config;

namespace Daedalus.Services.Eureka;

/// <summary>Pure Eureka decisions, fed live values by <see cref="EurekaService"/>.</summary>
public static class EurekaRules
{
    /// <summary>The four Eureka zones (TerritoryType): Anemos, Pagos, Pyros, Hydatos (the Baldesion Arsenal is Hydatos).</summary>
    public static bool IsEurekaTerritory(uint territory) => territory is 732 or 763 or 795 or 827;

    /// <summary>
    /// <c>ForayInfo.Element</c> as the game numbers elements everywhere else (Action.Aspect, EurekaState): 1 Fire,
    /// 2 Ice, 3 Wind, 4 Earth, 5 Lightning, 6 Water. Inferred, not yet confirmed on a live mob.
    /// </summary>
    public const byte Wind = 3;

    private static readonly string[] ElementNames = ["None", "Fire", "Ice", "Wind", "Earth", "Lightning", "Water"];

    public static string ElementName(byte element) => element < ElementNames.Length ? ElementNames[element] : $"#{element}";

    /// <summary>
    /// How long after arriving in Eureka before /magiaauto is sent: sent 1.2 s after zone-in, the game answered "The
    /// command "/magiaauto defense" is unavailable at this time." (Korha 2026-11-04), and the board was never set.
    /// </summary>
    public static readonly TimeSpan SettleAfterArrival = TimeSpan.FromSeconds(8);

    /// <summary>How long to wait before sending again after the game refused it.</summary>
    public static readonly TimeSpan RetryAfterRefusal = TimeSpan.FromSeconds(10);

    /// <summary>Whether a chat line is the game refusing a /magiaauto we sent.</summary>
    public static bool IsMagiaAutoRefused(string? message)
        => message != null && message.Contains("/magiaauto", StringComparison.OrdinalIgnoreCase)
                           && message.Contains("unavailable", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The /magiaauto setting wanted now; null = leave the board alone. TankDefense goes by the job alone: a tank is
    /// always on defense, everyone else on attack. (It used to put a tank on defense only while enemies were on it,
    /// which turned the board at every pull and between them: eight turns in eleven minutes, Korha 2026-11-01.)
    /// </summary>
    public static string? WantedAuto(EurekaMagiaMode mode, bool isTank) => mode switch
    {
        EurekaMagiaMode.Attack => "attack",
        EurekaMagiaMode.Defense => "defense",
        EurekaMagiaMode.TankDefense => isTank ? "defense" : "attack",
        _ => null,
    };

    /// <summary>Feint L is renewed with this little Evasion Down left (it lasts 60 s).</summary>
    public const float FeintRefreshSeconds = 3f;

    /// <summary>
    /// Feint L: only on a Wind-aligned enemy (the ones with raised evasion, as BMR's Eureka module does), and only
    /// when its Evasion Down is missing or nearly gone. <paramref name="element"/> null = not read.
    /// </summary>
    public static bool WantsFeint(byte? element, float? evasionDownLeft)
        => element == Wind && (evasionDownLeft is null || evasionDownLeft < FeintRefreshSeconds);

    /// <summary>Bloodbath L below this HP (BMR's threshold).</summary>
    public const float BloodbathHpPct = 0.5f;

    /// <summary>Bloodbath L: our HP is low and its drain is not already up.</summary>
    public static bool WantsBloodbath(float hpPct, bool hasBloodbath) => !hasBloodbath && hpPct < BloodbathHpPct;

    /// <summary>
    /// Whether an enemy is too far above us to pull. Unknown levels (0, outside Eureka or not read) never block.
    /// </summary>
    public static bool TooHighToPull(int myLevel, int enemyLevel, int maxLevelsAbove)
        => myLevel > 0 && enemyLevel > 0 && enemyLevel - myLevel > Math.Max(0, maxLevelsAbove);
}
