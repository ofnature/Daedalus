using System.Collections.Generic;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>
/// The line the horn setter logs for each battle: how the horns compare with the plan. Three First Board degree
/// fights on 2026-11-08 ran without the Wespe their plan names, and nothing recorded why.
/// </summary>
public class CrucibleHornPlanNoteTests
{
    private const int Wespe = 10, IceGolem = 47, Behemoth = 50, Damselfly = 44;

    private static string Name(int no) => no switch
    {
        Wespe => "Wespe", IceGolem => "Ice Golem", Behemoth => "Behemoth", Damselfly => "Damselfly", _ => $"#{no}",
    };

    private static readonly int[] Plan = [Wespe, IceGolem, Behemoth];

    private static string Note(IReadOnlyList<CrucibleTeamMember> team, IReadOnlyList<int>? plan, params int[] horns)
        => CrucibleHornPlan.PlanNote(team, plan, horns, Name);

    [Fact]
    public void ThePlanOnTheHornsIsAsPlanned()
        => Assert.Equal("as planned", Note([new(Wespe, true), new(IceGolem, true), new(Behemoth, true)], Plan, 0, 1, 2));

    [Fact]
    public void ADownBeastIsNamed()
        => Assert.Equal("plan Wespe > Ice Golem > Behemoth; Wespe is down",
            Note([new(Wespe, false), new(IceGolem, true), new(Behemoth, true), new(Damselfly, true)], Plan, 1, 2, 3));

    [Fact]
    public void AWornBeastIsNamedWithItsHp()
        => Assert.Contains("Wespe is worn (12",
            Note([new(Wespe, true, Health: 0.12f), new(IceGolem, true), new(Behemoth, true), new(Damselfly, true)], Plan, 1, 2, 3));

    [Fact]
    public void ABeastNotOnTheTeamIsNamed()
        => Assert.Contains("Behemoth is not on the team", Note([new(Wespe, true), new(IceGolem, true), new(Damselfly, true)], Plan, 0, 1, 2));

    [Fact]
    public void TheSameBeastsInAnotherOrderSaysSo()
        => Assert.StartsWith("the plan's beasts in another order",
            Note([new(Wespe, true), new(IceGolem, true), new(Behemoth, true)], Plan, 1, 2, 0));

    [Fact]
    public void NoPlanSaysSo()
    {
        Assert.Equal("no plan for this battle (picked by weakness)", Note([new(Wespe, true)], null, 0));
        Assert.Equal("no plan for this battle (picked by weakness)", Note([new(Wespe, true)], [0, 0, 0], 0));
    }
}
