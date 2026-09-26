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

    /// <summary>
    /// Only open a prophecy on a target expected to live at least this long: then even the latest
    /// commit (<see cref="ForceCommitSeconds"/>) comes while the fight is still on.
    /// </summary>
    public const float PredictMinTtkSeconds = ForceCommitSeconds;

    /// <summary>
    /// With no HP-loss samples yet the time-to-kill is unknown — which is every fresh pull. A target at
    /// or above this much HP counts as a fresh pull, so Predict can still open a fight.
    /// </summary>
    public const float FreshPullHpPct = 0.90f;

    /// <summary>A prophecy still open when the target is this close to dying is committed now.</summary>
    public const float FightEndingTtkSeconds = 5f;

    /// <summary>
    /// Whether to open a prophecy. Every prophecy that expires unplayed kills the Oracle (False
    /// Prediction), and the likeliest way to leave one unplayed is to open it as the fight ends, then
    /// mount up or leave the layer's reach. So only on a target that will outlive the whole window.
    /// </summary>
    public static bool ShouldPredict(bool inCombat, bool hasTarget, float targetTtkSeconds, float targetHpPct)
        => inCombat && hasTarget
           && (targetTtkSeconds == float.MaxValue
               ? targetHpPct >= FreshPullHpPct
               : targetTtkSeconds >= PredictMinTtkSeconds);

    /// <summary>
    /// The fight is ending under an open prophecy: out of combat, no target left, or the target about
    /// to die. Play whatever is offered rather than wait for a better card.
    /// </summary>
    public static bool FightEnding(bool inCombat, bool hasTarget, float targetTtkSeconds)
        => !inCombat || !hasTarget || targetTtkSeconds < FightEndingTtkSeconds;

    /// <summary>
    /// False Prediction is on this member and Invulnerability isn't: cast it. Invulnerability keeps HP
    /// from dropping below 1 for 8s, the one answer to a 50,000-potency DoT.
    /// </summary>
    public static bool NeedsInvulnerabilityForFalsePrediction(bool hasFalsePrediction, bool hasInvulnerability)
        => hasFalsePrediction && !hasInvulnerability;

    public static OracleDecision Decide(
        uint cardActionId,
        PhantomConfig cfg,
        bool lastCard,
        float windowElapsedSeconds,
        float selfHpPct,
        float partyAvgHpPct,
        bool invulnBuffUp,
        bool invulnReady,
        bool fightEnding = false)
    {
        var mustCommit = lastCard || windowElapsedSeconds >= ForceCommitSeconds || fightEnding;

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
