using Daedalus.Config;
using Daedalus.Rotation.Phantom;
using Xunit;

namespace Daedalus.Tests.Rotation.Phantom;

/// <summary>
/// Death-prevention tests for the Oracle card policy. Field death 2026-07-25: letting
/// every prediction expire turns the prophecy into False Prediction (50,000 potency to
/// self — always lethal). Past the force-commit point a card must ALWAYS be played,
/// overriding config toggles and the Starfall safety gate.
/// </summary>
public class OracleCardPolicyTests
{
    private static PhantomConfig Cfg() => new();

    private static OracleDecision Decide(
        uint card, PhantomConfig cfg, bool lastCard = false, float elapsed = 5f,
        float selfHp = 1f, float partyAvg = 1f, bool invulnUp = false, bool invulnReady = false)
        => OracleCardPolicy.Decide(card, cfg, lastCard, elapsed, selfHp, partyAvg, invulnUp, invulnReady);

    [Fact]
    public void JudgmentAndCleansing_CommitOnOffer()
    {
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.JudgmentCard, Cfg()));
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.CleansingCard, Cfg()));
    }

    [Fact]
    public void DisabledCard_StillPlaysWhenForced_NeverFalsePrediction()
    {
        var cfg = Cfg();
        cfg.OracleUseJudgment = false;
        cfg.OracleUseCleansing = false;
        cfg.OracleUseBlessing = false;
        cfg.OracleUseStarfall = false;

        // Early in the window a disabled card waits…
        Assert.Equal(OracleDecision.Wait, Decide(OracleCardPolicy.JudgmentCard, cfg));
        // …but on the last card / past force-commit it plays regardless of config.
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.JudgmentCard, cfg, lastCard: true));
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.CleansingCard, cfg, elapsed: 25f));
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.BlessingCard, cfg, elapsed: 25f));
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.StarfallCard, cfg, lastCard: true, selfHp: 1f));
    }

    [Fact]
    public void Blessing_HeldUntilHealNeeded_ThenPlays()
    {
        Assert.Equal(OracleDecision.Wait, Decide(OracleCardPolicy.BlessingCard, Cfg(), partyAvg: 0.95f, selfHp: 0.95f));
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.BlessingCard, Cfg(), partyAvg: 0.40f));
    }

    [Fact]
    public void Starfall_SafeAtHighHpOrUnderInvuln()
    {
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.StarfallCard, Cfg(), selfHp: 0.95f));
        Assert.Equal(OracleDecision.PlayCard, Decide(OracleCardPolicy.StarfallCard, Cfg(), selfHp: 0.30f, invulnUp: true));
    }

    [Fact]
    public void Starfall_UnsafeWithInvulnReady_CastsInvulnFirst()
    {
        Assert.Equal(OracleDecision.CastInvulnerability,
            Decide(OracleCardPolicy.StarfallCard, Cfg(), selfHp: 0.50f, invulnReady: true));
        // Forced with invuln ready: still take the safe route.
        Assert.Equal(OracleDecision.CastInvulnerability,
            Decide(OracleCardPolicy.StarfallCard, Cfg(), selfHp: 0.50f, invulnReady: true, lastCard: true));
    }

    [Fact]
    public void Starfall_ForcedWithNoInvuln_FiresAnyway_BetterThanCertainDeath()
    {
        Assert.Equal(OracleDecision.PlayCard,
            Decide(OracleCardPolicy.StarfallCard, Cfg(), selfHp: 0.30f, lastCard: true, invulnReady: false));
        Assert.Equal(OracleDecision.PlayCard,
            Decide(OracleCardPolicy.StarfallCard, Cfg(), selfHp: 0.30f, elapsed: 25f, invulnReady: false));
        // Not forced yet: waiting for rotation is fine.
        Assert.Equal(OracleDecision.Wait,
            Decide(OracleCardPolicy.StarfallCard, Cfg(), selfHp: 0.30f, elapsed: 5f, invulnReady: false));
    }

    [Fact]
    public void ForceCommitPoint_LeavesLandingMargin()
    {
        // 30s prophecy — committing at 20s leaves 10s to actually land the action.
        Assert.True(OracleCardPolicy.ForceCommitSeconds <= 25f);
    }
}
