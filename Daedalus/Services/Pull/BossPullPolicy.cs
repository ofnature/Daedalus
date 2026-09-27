namespace Daedalus.Services.Pull;

/// <summary>
/// Everything the boss-pull decision needs, gathered by <see cref="BossPullService"/> from live game state. Pure data
/// so the policy is unit-testable.
/// </summary>
public readonly record struct BossPullSituation(
    bool Enabled,
    ulong PullTargetId,
    bool PullTargetFound,
    bool SelfInCombat,
    bool Mounted,
    ulong CurrentTargetId,
    bool CurrentTargetIsLiveEnemy,
    double SecondsSinceLastSet);

/// <summary>
/// Pulling the boss nobody else will. The boss engine names it (<c>Minerva.Hints.PullTarget</c>): a boss fight that has
/// not started, with no other player in the party -- only Trust or Duty Support NPCs, who wait for you. We target it and
/// the rotation opens on it from where the toon stands, which for a caster or ranged is already in range.
/// <para>The Porta Decumana, 2026-09-27: an Astrologian stood 23y from Ultima for 49 seconds and a Machinist sat idle
/// until his target was set by hand, whereupon he attacked it straight away.</para>
/// </summary>
public static class BossPullPolicy
{
    /// <summary>Between target sets, so a target the game refuses is not re-set every frame.</summary>
    public const double RetargetSeconds = 1.0;

    /// <summary>
    /// Put the named boss on the toon's target: out of combat, not mounted, and only when the toon is not already on it
    /// or deliberately on another enemy.
    /// </summary>
    public static bool ShouldTarget(in BossPullSituation s)
        => s.Enabled
            && s.PullTargetId != 0
            && s.PullTargetFound
            && !s.SelfInCombat
            && !s.Mounted
            && s.CurrentTargetId != s.PullTargetId
            && !s.CurrentTargetIsLiveEnemy
            && s.SecondsSinceLastSet >= RetargetSeconds;

    /// <summary>
    /// The rotation may open on its hard target out of combat because that target is the boss to pull -- the same
    /// engage an automation plugin's combat override gives, for this one boss only.
    /// </summary>
    public static bool Opens(bool enabled, ulong pullTargetId, ulong hardTargetId)
        => enabled && pullTargetId != 0 && hardTargetId == pullTargetId;
}
