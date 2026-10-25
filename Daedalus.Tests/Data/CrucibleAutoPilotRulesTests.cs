using System;
using Daedalus.Data;
using Xunit;

namespace Daedalus.Tests.Data;

/// <summary>The Crucible autopilot's decisions: loot order, which prompts it answers, campsite pick, battle start.</summary>
public class CrucibleAutoPilotRulesTests
{
    [Theory]
    [InlineData(true, false, false, CrucibleLootStep.TakeItem)]
    [InlineData(true, true, false, CrucibleLootStep.TakeTokens)]
    [InlineData(false, false, false, CrucibleLootStep.TakeTokens)]
    [InlineData(false, false, true, CrucibleLootStep.Leave)]
    [InlineData(true, true, true, CrucibleLootStep.Leave)]
    public void LootIsItemThenTokensThenLeave(bool takeItems, bool itemTried, bool tokensTried, CrucibleLootStep expected)
        => Assert.Equal(expected, CrucibleAutoPilotRules.NextLootStep(takeItems, itemTried, tokensTried));

    /// <summary>Only the prompt its own click raised: the recorded wording, nothing else.</summary>
    [Theory]
    [InlineData("Take 203 territory tokens?", "Take ", true)]
    [InlineData("Some loot remains unclaimed. Leave it behind?", "Some loot remains unclaimed.", true)]
    [InlineData("Conclude purchasing and leave the shop? / You cannot return to the shop after leaving.", "Conclude purchasing and leave", true)]
    [InlineData("Rest and recover 45% of HP for you and your: / ・ Damselfly / Proceed?", "Rest ", true)]
    [InlineData("Rest alone while your familiars keep watch and recover 90% of HP?", "Rest ", true)] // field 2026-10-25: was left unanswered
    [InlineData("Take 203 territory tokens?", null, false)]                        // nothing expected
    [InlineData("Forfeit the board?", "Take ", false)]                             // a different prompt
    [InlineData("Discard changes and return to board selection?", "Discard", false)] // not a known prompt
    public void OnlyItsOwnPromptsAreConfirmed(string prompt, string? expected, bool confirm)
        => Assert.Equal(confirm, CrucibleAutoPilotRules.ShouldConfirm(prompt, expected));

    /// <summary>The most hurt standing beast rests; a fallen one (0 HP) is passed over.</summary>
    [Fact]
    public void TheMostHurtStandingBeastRests()
        => Assert.Equal(2, CrucibleAutoPilotRules.CampsitePick([(500, 535), (0, 535), (100, 757), (700, 700)]));

    /// <summary>Field 2026-10-25: under half HP you rest alone (90%, not 45%) — your HP is the run.</summary>
    [Fact]
    public void UnderHalfHpYouRestAlone()
        => Assert.Equal(-1, CrucibleAutoPilotRules.CampsitePick([(500, 535), (100, 757)], selfHpPct: 0.13f));

    [Fact]
    public void AtHalfHpOrMoreTheMostHurtBeastShares()
        => Assert.Equal(1, CrucibleAutoPilotRules.CampsitePick([(500, 535), (100, 757)], selfHpPct: 0.6f));

    [Fact]
    public void NobodyStandingMeansNoCampsitePick()
        => Assert.Equal(-1, CrucibleAutoPilotRules.CampsitePick([(0, 535), (0, 757)]));

    [Fact]
    public void TheBattleStartsOnceTheHornsHaveStood()
    {
        Assert.True(CrucibleAutoPilotRules.ShouldStartBattle(7, 7, TimeSpan.FromSeconds(2), alreadyStarted: 0));
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 7, TimeSpan.FromSeconds(0.5), alreadyStarted: 0)); // too soon
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 0, TimeSpan.FromSeconds(9), alreadyStarted: 0));   // horns not set
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 6, TimeSpan.FromSeconds(9), alreadyStarted: 0));   // set for another battle
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(7, 7, TimeSpan.FromSeconds(9), alreadyStarted: 7));   // already started
        Assert.False(CrucibleAutoPilotRules.ShouldStartBattle(0, 0, TimeSpan.FromSeconds(9), alreadyStarted: 0));   // no battle shown
    }
}
