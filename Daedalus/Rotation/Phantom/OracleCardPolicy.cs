using Daedalus.Config;

namespace Daedalus.Rotation.Phantom;

public enum OracleDecision
{
    Wait,
    PlayCard,
    CastInvulnerability,
}

/// <summary>
/// Pure decision policy for the offered Oracle card. THE cardinal rule (field death
/// 2026-07-25): if every prediction expires unplayed, the prophecy becomes False
/// Prediction — 50,000 potency to self, a guaranteed kill. So past the force-commit
/// point (or on the last card) SOMETHING is always played, overriding config toggles
/// and even the Starfall safety gate — a survivable-maybe Starfall beats a certain
/// False Prediction death.
/// </summary>
public static class OracleCardPolicy
{
    /// <summary>The prophecy lasts 30s total; commit no later than this into the window.</summary>
    public const float ForceCommitSeconds = 20f;

    public const uint JudgmentCard = 41637;
    public const uint CleansingCard = 41638;
    public const uint BlessingCard = 41639;
    public const uint StarfallCard = 41640;

    /// <summary>Self HP fraction above which Starfall's self-damage is considered survivable.</summary>
    public const float StarfallSafeHpPct = 0.90f;

    public static OracleDecision Decide(
        uint cardActionId,
        PhantomConfig cfg,
        bool lastCard,
        float windowElapsedSeconds,
        float selfHpPct,
        float partyAvgHpPct,
        bool invulnBuffUp,
        bool invulnReady)
    {
        var mustCommit = lastCard || windowElapsedSeconds >= ForceCommitSeconds;

        switch (cardActionId)
        {
            case JudgmentCard:
                return cfg.OracleUseJudgment || mustCommit ? OracleDecision.PlayCard : OracleDecision.Wait;

            case CleansingCard:
                return cfg.OracleUseCleansing || mustCommit ? OracleDecision.PlayCard : OracleDecision.Wait;

            case BlessingCard:
                if (mustCommit)
                    return OracleDecision.PlayCard;
                var healNeeded = partyAvgHpPct < cfg.OracleBlessingPartyHpPct || selfHpPct < cfg.OracleBlessingPartyHpPct;
                return cfg.OracleUseBlessing && healNeeded ? OracleDecision.PlayCard : OracleDecision.Wait;

            case StarfallCard:
                if (invulnBuffUp || selfHpPct > StarfallSafeHpPct)
                    return cfg.OracleUseStarfall || mustCommit ? OracleDecision.PlayCard : OracleDecision.Wait;
                if (invulnReady && (mustCommit || (cfg.OracleUseStarfall && cfg.OracleSaveInvulnForStarfall)))
                    return OracleDecision.CastInvulnerability;
                // Unsafe, no invuln available: forced → fire anyway (better odds than
                // False Prediction); otherwise wait for the deck to rotate.
                return mustCommit ? OracleDecision.PlayCard : OracleDecision.Wait;

            default:
                return OracleDecision.Wait;
        }
    }
}
