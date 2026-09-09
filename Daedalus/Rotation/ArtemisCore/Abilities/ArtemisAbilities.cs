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
