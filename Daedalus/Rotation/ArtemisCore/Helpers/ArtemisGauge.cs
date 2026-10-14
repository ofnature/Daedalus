using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// One frame of the Beastmaster job gauge.
/// <para>
/// ClientStructs had no Beastmaster gauge until <c>78975d83</c> (shipped with Dalamud 15.0.3.7 for patch
/// 7.57). Everything Artemis knew about TP, the instinct diamonds and the Inner Compass before that was
/// inferred — from whether the game would accept an action, and from counting its own presses. This is the
/// real thing, and wherever it is available the inference stands down.
/// </para>
/// <para>
/// A plain value copied out of the game each frame, so the rules that read it stay testable without one.
/// </para>
/// </summary>
/// <param name="Tp">The beastmaster's TP, 0-250. The axes need 100 and spend all of it.</param>
/// <param name="PetTp">The familiar's TP, 0-250. Trick needs 100.</param>
/// <param name="PetTpAtLastUse">Familiar TP when Trick was last used (the game keeps it for the potency readout).</param>
/// <param name="ActiveBattlehorn">Raw byte. Shown, not acted on: its numbering (0- or 1-based, what "none" is) is unverified.</param>
/// <param name="ComboState">Raw byte. Shown, not acted on: meaning unverified.</param>
/// <param name="Affinity">What is lit on the Inner Compass: one of the four Hearts, Sunstrider, Moonstalker, or None.</param>
/// <param name="ChainCount">Instinctual skills in the current chain.</param>
/// <param name="KinshipBattlehorn">Raw nibble. Shown only.</param>
/// <param name="KinshipClassification">Raw nibble. Shown only.</param>
/// <param name="NaturalInstinct">Blue diamonds, 0-3. Rallying Cheer spends them.</param>
/// <param name="MasteredInstinct">Yellow diamonds, 0-3. Rally spends them.</param>
public readonly record struct ArtemisGauge(
    int Tp,
    int PetTp,
    int PetTpAtLastUse,
    int ActiveBattlehorn,
    int ComboState,
    InstinctAffinity Affinity,
    int ChainCount,
    int KinshipBattlehorn,
    int KinshipClassification,
    int NaturalInstinct,
    int MasteredInstinct)
{
    /// <summary>"Minimum TP Gauge Cost: 100" on every coloured axe, and the same on Trick for the familiar.</summary>
    public const int MinSpendTp = 100;

    /// <summary>"TP Gauge Cost: 250" — the upgraded skills, and the cap of both bars.</summary>
    public const int MaxTp = 250;

    /// <summary>One of the four Hearts is lit: an instinctual skill went out inside the last seven seconds.</summary>
    public bool HasHeart => Affinity is InstinctAffinity.Volant or InstinctAffinity.Rampant
        or InstinctAffinity.Durant or InstinctAffinity.Eldritch;

    /// <summary>Half of the centre is filled — Sunstrider or Moonstalker — and waiting for the other.</summary>
    public bool HasHalf => Affinity is InstinctAffinity.Sunstrider or InstinctAffinity.Moonstalker;

    /// <summary>One line for the Debug window.</summary>
    public string Describe()
        => $"TP {Tp} · pet TP {PetTp} · Mastered {MasteredInstinct} · Natural {NaturalInstinct} · "
           + $"lit {Affinity} · chain {ChainCount} · horn {ActiveBattlehorn} · combo {ComboState} · "
           + $"kinship {KinshipClassification}/{KinshipBattlehorn}";
}
