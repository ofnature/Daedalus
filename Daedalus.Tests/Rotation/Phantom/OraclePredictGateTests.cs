using Daedalus.Config;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// A prophecy must never expire unplayed (False Prediction: 50,000 potency to self). The two ways it
/// did: opening one as the fight ended, and then waiting for a better card while the toon mounted up.
/// </summary>
public class OraclePredictGateTests
{
    private const float Unknown = float.MaxValue;

    [Theory]
    [InlineData(true, true, 30f, 0.5f, true)]    // long fight left
    [InlineData(true, true, OracleCardPolicy.PredictMinTtkSeconds, 0.5f, true)]
    [InlineData(true, true, 12f, 0.5f, false)]   // dies before the latest commit
    [InlineData(true, true, Unknown, 1.0f, true)] // fresh pull, no samples yet
    [InlineData(true, true, Unknown, 0.3f, false)] // unknown on a half-dead target: not a fresh pull
    [InlineData(true, false, 30f, 1.0f, false)]  // no target
    [InlineData(false, true, 30f, 1.0f, false)]  // out of combat
    public void Predict_OnlyWhenTheFightOutlivesTheProphecy(bool inCombat, bool hasTarget, float ttk, float hp, bool predict)
        => Assert.Equal(predict, OracleCardPolicy.ShouldPredict(inCombat, hasTarget, ttk, hp));

    [Theory]
    [InlineData(false, true, 30f, true)]   // combat over
    [InlineData(true, false, 30f, true)]   // target gone
    [InlineData(true, true, 3f, true)]     // target about to die
    [InlineData(true, true, 30f, false)]
    [InlineData(true, true, Unknown, false)]
    public void FightEnding(bool inCombat, bool hasTarget, float ttk, bool ending)
        => Assert.Equal(ending, OracleCardPolicy.FightEnding(inCombat, hasTarget, ttk));

    /// <summary>Blessing normally waits for someone to be hurt; with the fight ending it plays now.</summary>
    [Fact]
    public void FightEnding_PlaysBlessingEvenWithEveryoneFull()
    {
        var cfg = new PhantomConfig();
        Assert.Equal(OracleDecision.Wait, OracleCardPolicy.Decide(
            OracleCardPolicy.BlessingCard, cfg, lastCard: false, windowElapsedSeconds: 2f,
            selfHpPct: 1f, partyAvgHpPct: 1f, invulnBuffUp: false, invulnReady: false));
        Assert.Equal(OracleDecision.PlayCard, OracleCardPolicy.Decide(
            OracleCardPolicy.BlessingCard, cfg, lastCard: false, windowElapsedSeconds: 2f,
            selfHpPct: 1f, partyAvgHpPct: 1f, invulnBuffUp: false, invulnReady: false, fightEnding: true));
    }

    /// <summary>An unsafe Starfall still beats a certain False Prediction once the fight is ending.</summary>
    [Fact]
    public void FightEnding_PlaysAnUnsafeStarfallRatherThanLetItExpire()
        => Assert.Equal(OracleDecision.PlayCard, OracleCardPolicy.Decide(
            OracleCardPolicy.StarfallCard, new PhantomConfig(), lastCard: false, windowElapsedSeconds: 2f,
            selfHpPct: 0.5f, partyAvgHpPct: 1f, invulnBuffUp: false, invulnReady: false, fightEnding: true));
}

/// <summary>Invulnerability (HP can't drop below 1 for 8s) against a False Prediction that landed anyway.</summary>
public class OracleInvulnerabilityOnFalsePredictionTests
{
    [Theory]
    [InlineData(true, false, true)]   // hit by False Prediction, unprotected: cast it
    [InlineData(true, true, false)]   // already covered: don't waste the recast
    [InlineData(false, false, false)] // nothing to answer
    public void CastWhenFalsePredictionIsUnanswered(bool falsePrediction, bool invulnerable, bool cast)
        => Assert.Equal(cast, OracleCardPolicy.NeedsInvulnerabilityForFalsePrediction(falsePrediction, invulnerable));
}

/// <summary>Healers put a False Prediction victim first, like a Doomed member.</summary>
public class PriorityHealDotTests
{
    [Fact]
    public void FalsePrediction_IsAPriorityHealDot()
        => Assert.True(HealerPartyHelper.IsPriorityHealDotStatusId(4269));

    [Theory]
    [InlineData(4265u)] // Prediction of Judgment: a card, not a DoT
    [InlineData(1769u)] // Doom has its own list
    [InlineData(0u)]
    public void OtherStatuses_AreNot(uint statusId)
        => Assert.False(HealerPartyHelper.IsPriorityHealDotStatusId(statusId));
}
