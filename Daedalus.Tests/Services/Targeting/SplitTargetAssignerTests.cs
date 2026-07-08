using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daedalus.Services.Targeting;
using Xunit;

namespace Daedalus.Tests.Services.Targeting;

/// <summary>
/// Pure Split assignment: TTK balancing (heavier mobs draw more toons), determinism across boxes,
/// melee locality tiebreak, and the degenerate single-enemy / more-toons cases.
/// </summary>
public class SplitTargetAssignerTests
{
    private static SplitEnemy Enemy(ulong id, float hp, float x = 0f)
        => new(id, hp, new Vector3(x, 0f, 0f));

    private static SplitToon Toon(string id, float dps = 1f, float x = 0f, bool melee = false)
        => new(id, dps, new Vector3(x, 0f, 0f), melee);

    [Fact]
    public void Assign_EmptyInputs_ReturnsEmpty()
    {
        Assert.Empty(SplitTargetAssigner.Assign(new List<SplitEnemy>(), new[] { Toon("a") }));
        Assert.Empty(SplitTargetAssigner.Assign(new[] { Enemy(1, 100) }, new List<SplitToon>()));
    }

    [Fact]
    public void Assign_TwoEqualMobs_TwoToons_OneEach()
    {
        var enemies = new[] { Enemy(100, 1000), Enemy(200, 1000) };
        var toons = new[] { Toon("a"), Toon("b") };

        var map = SplitTargetAssigner.Assign(enemies, toons);

        Assert.Equal(2, map.Count);
        Assert.NotEqual(map["a"], map["b"]); // spread, not stacked
    }

    [Fact]
    public void Assign_BiggerMob_DrawsMoreToons_ForBalancedTtk()
    {
        // Big mob has 2x HP; with 3 equal-DPS toons it should get 2 and the small mob 1,
        // so both die at ~the same time.
        var enemies = new[] { Enemy(100, 2000), Enemy(200, 1000) };
        var toons = new[] { Toon("a"), Toon("b"), Toon("c") };

        var map = SplitTargetAssigner.Assign(enemies, toons);

        var onBig = map.Values.Count(id => id == 100);
        var onSmall = map.Values.Count(id => id == 200);
        Assert.Equal(2, onBig);
        Assert.Equal(1, onSmall);
    }

    [Fact]
    public void Assign_IsDeterministic_AcrossRuns()
    {
        var enemies = new[] { Enemy(300, 1500), Enemy(100, 900), Enemy(200, 1200) };
        var toons = new[] { Toon("z", 2f), Toon("a", 1f), Toon("m", 3f) };

        var first = SplitTargetAssigner.Assign(enemies, toons);
        var second = SplitTargetAssigner.Assign(enemies, toons);

        Assert.Equal(first.OrderBy(k => k.Key), second.OrderBy(k => k.Key));
    }

    [Fact]
    public void Assign_MeleeLocality_PrefersNearerMob_WhenBalanceTied()
    {
        // Two identical mobs; a single melee toon sits next to mob 100 (far from 200).
        var enemies = new[] { Enemy(100, 1000, x: 0f), Enemy(200, 1000, x: 100f) };
        var toons = new[] { Toon("melee", dps: 1f, x: 1f, melee: true) };

        var map = SplitTargetAssigner.Assign(enemies, toons);

        Assert.Equal(100ul, map["melee"]);
    }

    [Fact]
    public void Assign_MoreToonsThanEnemies_AllToOneEnemy()
    {
        var enemies = new[] { Enemy(100, 1000) };
        var toons = new[] { Toon("a"), Toon("b"), Toon("c") };

        var map = SplitTargetAssigner.Assign(enemies, toons);

        Assert.Equal(3, map.Count);
        Assert.All(map.Values, id => Assert.Equal(100ul, id));
    }
}
