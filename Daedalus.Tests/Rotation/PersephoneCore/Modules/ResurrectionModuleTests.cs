using Moq;
using Daedalus.Data;
using Daedalus.Rotation.PersephoneCore.Abilities;
using Daedalus.Rotation.PersephoneCore.Modules;
using Daedalus.Tests.Mocks;
using Daedalus.Tests.Rotation.Common.Scheduling;
using Xunit;

namespace Daedalus.Tests.Rotation.PersephoneCore.Modules;

/// <summary>
/// SMN battle rez (raid audit 2026-07-18 — Summoner previously had NO raise handling).
/// The positive raise path needs GameObject-backed party mocks (the documented raise-test
/// limitation shared with the healers); these pin the wiring, gates, and priority ordering.
/// </summary>
public class ResurrectionModuleTests
{
    [Fact]
    public void ResurrectionModule_RunsBeforeBuffsAndDamage()
    {
        var resurrection = new ResurrectionModule();
        Assert.True(resurrection.Priority < new BuffModule(null).Priority);
        Assert.True(resurrection.Priority < new DamageModule(null, null).Priority);
    }

    [Fact]
    public void ResurrectionAbility_UsesArcanistRez_GatedOnGlobalRaiseToggle()
    {
        Assert.Same(RoleActions.Resurrection, PersephoneAbilities.Resurrection.Action);
        Assert.NotNull(PersephoneAbilities.Resurrection.Toggle);

        var config = new Configuration();
        config.Resurrection.EnableRaise = true;
        Assert.True(PersephoneAbilities.Resurrection.Toggle!(config));
        config.Resurrection.EnableRaise = false;
        Assert.False(PersephoneAbilities.Resurrection.Toggle!(config));
    }

    [Fact]
    public void NoDeadMembers_PushesNothing_RotationUndisturbed()
    {
        var context = PersephoneTestContext.Create(inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest();

        new ResurrectionModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
        Assert.Empty(scheduler.InspectOgcdQueue());
    }

    [Fact]
    public void RaiseDisabled_PushesNothing()
    {
        var config = new Configuration();
        config.Resurrection.EnableRaise = false;
        var context = PersephoneTestContext.Create(config: config, inCombat: true);
        var scheduler = SchedulerFactory.CreateForTest(config: config);

        new ResurrectionModule().CollectCandidates(context, scheduler, isMoving: false);

        Assert.Empty(scheduler.InspectGcdQueue());
        Assert.Empty(scheduler.InspectOgcdQueue());
    }
}
