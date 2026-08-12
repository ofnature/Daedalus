using Daedalus.Data;
using Daedalus.Services.Network;
using Daedalus.Services.Party;
using Xunit;

namespace Daedalus.Tests.Services.Party;

/// <summary>
/// The fleet limit-break call names a ROLE, so the whole feature rests on each job answering for
/// exactly one — a job that answers for two would fire on someone else's call and spend the
/// party's bar on the wrong effect.
/// </summary>
public sealed class LimitBreakPolicyTests
{
    [Theory]
    [InlineData(JobRegistry.Paladin, LimitBreakRole.Tank)]
    [InlineData(JobRegistry.Warrior, LimitBreakRole.Tank)]
    [InlineData(JobRegistry.DarkKnight, LimitBreakRole.Tank)]
    [InlineData(JobRegistry.Gunbreaker, LimitBreakRole.Tank)]
    [InlineData(JobRegistry.WhiteMage, LimitBreakRole.Healer)]
    [InlineData(JobRegistry.Scholar, LimitBreakRole.Healer)]
    [InlineData(JobRegistry.Astrologian, LimitBreakRole.Healer)]
    [InlineData(JobRegistry.Sage, LimitBreakRole.Healer)]
    [InlineData(JobRegistry.Samurai, LimitBreakRole.Melee)]
    [InlineData(JobRegistry.Ninja, LimitBreakRole.Melee)]
    [InlineData(JobRegistry.Monk, LimitBreakRole.Melee)]
    [InlineData(JobRegistry.Bard, LimitBreakRole.RangedPhysical)]
    [InlineData(JobRegistry.Machinist, LimitBreakRole.RangedPhysical)]
    [InlineData(JobRegistry.Dancer, LimitBreakRole.RangedPhysical)]
    [InlineData(JobRegistry.BlackMage, LimitBreakRole.Caster)]
    [InlineData(JobRegistry.Summoner, LimitBreakRole.Caster)]
    [InlineData(JobRegistry.RedMage, LimitBreakRole.Caster)]
    [InlineData(JobRegistry.Pictomancer, LimitBreakRole.Caster)]
    public void RoleFor_ClassifiesEveryCombatJob(uint jobId, LimitBreakRole expected)
    {
        Assert.Equal(expected, LimitBreakPolicy.RoleFor(jobId));
        Assert.True(LimitBreakPolicy.Answers(expected, jobId));
    }

    /// <summary>
    /// The point of the role gate: a call for one role must be dropped by every other box, or
    /// four toons race for a bar only one of them can spend.
    /// </summary>
    [Fact]
    public void Answers_IsFalseForEveryOtherRole()
    {
        foreach (var call in new[]
                 {
                     LimitBreakRole.Tank, LimitBreakRole.Healer, LimitBreakRole.Melee,
                     LimitBreakRole.RangedPhysical, LimitBreakRole.Caster,
                 })
        {
            Assert.False(LimitBreakPolicy.Answers(call, JobRegistry.Paladin) && call != LimitBreakRole.Tank);
            Assert.False(LimitBreakPolicy.Answers(call, JobRegistry.Sage) && call != LimitBreakRole.Healer);
            Assert.False(LimitBreakPolicy.Answers(call, JobRegistry.Samurai) && call != LimitBreakRole.Melee);
            Assert.False(LimitBreakPolicy.Answers(call, JobRegistry.Bard) && call != LimitBreakRole.RangedPhysical);
            Assert.False(LimitBreakPolicy.Answers(call, JobRegistry.BlackMage) && call != LimitBreakRole.Caster);
        }
    }

    /// <summary>A job with no limit break answers for nothing rather than defaulting into a role.</summary>
    [Fact]
    public void RoleFor_IsNullForNonCombatAndUnsetJobs()
    {
        Assert.Null(LimitBreakPolicy.RoleFor(0));
        Assert.Null(LimitBreakPolicy.RoleFor(JobRegistry.Carpenter));
        Assert.Null(LimitBreakPolicy.RoleFor(JobRegistry.Fisher));
    }

    /// <summary>
    /// The call is retried rather than fired once: the datagram lands on an arbitrary frame and
    /// the bar can legitimately be uncastable right then.
    /// </summary>
    [Fact]
    public void ArmWindow_LeavesRoomToRetry()
    {
        Assert.True(LimitBreakPolicy.ArmWindowSeconds >= 3f);
        Assert.True(LimitBreakPolicy.RetryIntervalSeconds > 0f);
        Assert.True(LimitBreakPolicy.RetryIntervalSeconds < LimitBreakPolicy.ArmWindowSeconds);
    }

    [Fact]
    public void Label_IsSetForEveryRole()
    {
        foreach (LimitBreakRole role in System.Enum.GetValues<LimitBreakRole>())
            Assert.False(string.IsNullOrWhiteSpace(LimitBreakPolicy.Label(role)));
    }

    /// <summary>The wire id is part of the protocol — shifting it silently breaks mixed fleets.</summary>
    [Fact]
    public void LimitBreakMessage_KeepsItsWireId()
    {
        Assert.Equal(20, (int)LanMessageType.LimitBreak);

        var round = LanLimitBreakPayload.FromJson(
            new LanLimitBreakPayload { Role = LimitBreakRole.Caster }.ToJson());
        Assert.NotNull(round);
        Assert.Equal(LimitBreakRole.Caster, round!.Role);
    }
}
