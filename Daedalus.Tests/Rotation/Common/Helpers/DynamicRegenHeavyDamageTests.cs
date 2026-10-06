using Daedalus.Rotation.Common.Helpers;
using Xunit;

namespace Daedalus.Tests.Rotation.Common.Helpers;

/// <summary>
/// "Heavy damage" raises the regen threshold to 95%. The flat 300 DPS floor alone is crossed by a boss's auto-attacks
/// on the tank, so Saar's Astrologian kept Aspected Benefic on a 90% tank all fight (Forgiven Dissonance, 2026-10-06:
/// ~17 of ~70 GCDs, 30% overheal). Heavy now also means the party losing 5% of its combined HP every second.
/// </summary>
public class DynamicRegenHeavyDamageTests
{
    private const float PartyHp = 70_000f; // four members at level 72

    [Fact]
    public void TankAutosAreNotHeavyDamage()
        => Assert.False(DynamicRegenThresholdHelper.IsHeavyDamage(partyDamageRate: 1_200f, flatFloor: 300f, PartyHp));

    [Fact]
    public void AWallToWallPullIs()
        => Assert.True(DynamicRegenThresholdHelper.IsHeavyDamage(partyDamageRate: 4_000f, flatFloor: 300f, PartyHp));

    [Fact]
    public void TheFlatFloorStillApplies()
        => Assert.False(DynamicRegenThresholdHelper.IsHeavyDamage(partyDamageRate: 250f, flatFloor: 300f, partyMaxHpTotal: 1_000f));

    /// <summary>With no party HP to go by, the flat floor alone decides, as before.</summary>
    [Fact]
    public void WithoutPartyHpTheFlatFloorDecides()
        => Assert.True(DynamicRegenThresholdHelper.IsHeavyDamage(partyDamageRate: 400f, flatFloor: 300f, partyMaxHpTotal: 0f));
}
