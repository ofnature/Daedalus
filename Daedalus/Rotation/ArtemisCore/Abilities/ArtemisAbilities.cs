using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.ArtemisCore.Abilities;

/// <summary>
/// Declarative <see cref="AbilityBehavior"/> for every action the Beastmaster rotation fires.
/// <para>
/// No role actions — Beastmaster has none at all, which is different from having none learned.
/// No Battlehorns either: which familiar to summon is the player's choice.
/// </para>
/// </summary>
public static class ArtemisAbilities
{
    // --- the 2.5s combo chain (real GCDs) ---
    public static readonly AbilityBehavior SmashAxe = new() { Action = BSTActions.SmashAxe };
    public static readonly AbilityBehavior AxebladeBite = new() { Action = BSTActions.AxebladeBite };
    public static readonly AbilityBehavior Shieldsplitter = new() { Action = BSTActions.Shieldsplitter };

    // --- instinctual skills: Weaponskills that do NOT consume the GCD, so they weave ---
    public static readonly AbilityBehavior AvalancheAxe = new()
        { Action = BSTActions.AvalancheAxe, Toggle = cfg => cfg.Beastmaster.EnableInstinctualSkills };
    public static readonly AbilityBehavior MistralAxe = new()
        { Action = BSTActions.MistralAxe, Toggle = cfg => cfg.Beastmaster.EnableInstinctualSkills };
    public static readonly AbilityBehavior SpinningAxe = new()
        { Action = BSTActions.SpinningAxe, Toggle = cfg => cfg.Beastmaster.EnableInstinctualSkills };
    public static readonly AbilityBehavior GaleAxe = new()
        { Action = BSTActions.GaleAxe, Toggle = cfg => cfg.Beastmaster.EnableInstinctualSkills };

    // --- familiar orders ---
    public static readonly AbilityBehavior Trick = new()
        { Action = BSTActions.Trick, Toggle = cfg => cfg.Beastmaster.EnableTrick };
    public static readonly AbilityBehavior PartingBlow = new()
        { Action = BSTActions.PartingBlow, Toggle = cfg => cfg.Beastmaster.EnablePartingBlow };

    /// <summary>
    /// The same Parting Blow, sent to retreat a familiar about to fall on a Crucible board. Its own toggle: behind
    /// EnablePartingBlow (opt-in, off by default) the scheduler dropped every retreat the module asked for, so no
    /// familiar was ever retreated before 2026-10-25.
    /// </summary>
    public static readonly AbilityBehavior PartingBlowRetreat = new()
        { Action = BSTActions.PartingBlow, Toggle = cfg => cfg.Beastmaster.CruciblePartingBlowWhenFalling };
    public static readonly AbilityBehavior TemperedRelease = new()
        { Action = BSTActions.TemperedRelease, Toggle = cfg => cfg.Beastmaster.EnableTemperedRelease };

    // --- the rest of the kit ---
    public static readonly AbilityBehavior Rally = new()
        { Action = BSTActions.Rally, Toggle = cfg => cfg.Beastmaster.EnableRally };
    public static readonly AbilityBehavior RallyingCheer = new()
        { Action = BSTActions.RallyingCheer, Toggle = cfg => cfg.Beastmaster.EnableRally };
    public static readonly AbilityBehavior ShieldCharge = new()
        { Action = BSTActions.ShieldCharge, Toggle = cfg => cfg.Beastmaster.EnableShieldCharge };
    public static readonly AbilityBehavior Borrow = new()
        { Action = BSTActions.Borrow, Toggle = cfg => cfg.Beastmaster.EnableBorrow };

    // --- the Crucible's duty actions ---
    public static readonly AbilityBehavior Snarl = new()
        { Action = BSTActions.Snarl, Toggle = cfg => cfg.Beastmaster.EnableSnarl };
    public static readonly AbilityBehavior Challenge = new()
        { Action = BSTActions.Challenge, Toggle = cfg => cfg.Beastmaster.EnableChallenge };

    // Beast Mode is sent by its base id with no status pre-check on that id: the usable action is whatever
    // the game has substituted, and the module has already asked the game about THAT one.
    public static readonly AbilityBehavior BeastMode = new()
    {
        Action = BSTActions.BeastMode, Toggle = cfg => cfg.Beastmaster.EnableBorrow,
        ReplacementBaseId = BSTActions.BeastMode.ActionId,
    };
    public static readonly AbilityBehavior BeastModeAtEnemy = new()
    {
        Action = BSTActions.BeastModeAtEnemy, Toggle = cfg => cfg.Beastmaster.EnableBorrow,
        ReplacementBaseId = BSTActions.BeastMode.ActionId,
    };

    // --- capture ---
    public static readonly AbilityBehavior Capture = new()
        { Action = BSTActions.Capture, Toggle = cfg => cfg.Beastmaster.EnableAutoCapture };

    /// <summary>The instinctual behavior carrying an affinity, or null.</summary>
    public static AbilityBehavior? InstinctualFor(InstinctAffinity affinity) => affinity switch
    {
        InstinctAffinity.Volant => GaleAxe,
        InstinctAffinity.Rampant => AvalancheAxe,
        InstinctAffinity.Durant => MistralAxe,
        InstinctAffinity.Eldritch => SpinningAxe,
        _ => null,
    };
}
