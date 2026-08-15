using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>Where one Phantom Blue Mage spell is learned.</summary>
/// <param name="ActionId">The phantom action.</param>
/// <param name="Spell">Display name.</param>
/// <param name="RequiredLevel">Phantom Blue Mage level needed before it can be learned.</param>
/// <param name="Enemy">The enemy that must use it in front of you, then die.</param>
/// <param name="Where">Zone location, or the critical encounter that spawns the enemy.</param>
public readonly record struct PhantomBlueMageSource(
    uint ActionId, string Spell, byte RequiredLevel, string Enemy, string Where);

/// <summary>
/// Which enemy teaches each Phantom Blue Mage spell.
/// <para>
/// REFERENCE DATA, NOT OBSERVATION — and deliberately kept apart from the elemental weakness
/// table, which is observational on purpose and must never be backfilled. This is a different
/// kind of fact: a fixed property of the content, and one that is only useful BEFORE you have
/// the spell. A list you can only earn by already having learned the spell would answer a
/// question nobody asks.
/// </para>
/// <para>
/// There is no game-data source for it. Checked 2026-08-14: <c>MKDBNpcData</c> carries a single
/// unnamed field, <c>AozAction</c> (the real Blue Mage learn table) has no Occult rows at all,
/// and the only unlock-shaped field on these actions is an <c>UnlockLink</c> flag that says
/// whether YOU have learned it, not who teaches it. So this is transcribed from the community
/// wiki, cited below. It is NOT taken from Another_Occult_Crescent_Helper, whose dataset is
/// AGPL-licensed.
/// </para>
/// <para>
/// Source: https://ffxiv.consolegameswiki.com/wiki/Phantom_Blue_Mage
/// </para>
/// </summary>
public static class PhantomBlueMageSources
{
    /// <summary>Occult Aero needs no hunt — it comes with the job.</summary>
    public const string UnlockedByDefault = "Unlocked by default";

    /// <summary>
    /// Every spell, in unlock order. Note that ALL of them are learned in North Horn — South Horn
    /// teaches this job nothing.
    /// </summary>
    public static readonly IReadOnlyList<PhantomBlueMageSource> All =
    [
        new(49085, "Occult Aero", 1, UnlockedByDefault, string.Empty),
        new(49086, "Occult Missile", 1, "Pallmagia", "Appalling Behavior (critical encounter)"),
        new(49087, "Occult Aqua Breath", 1, "Crescent Stoneshell", "North Horn (X:31, Y:8)"),
        new(49089, "Occult Aero II", 2, "Crescent Anila", "North Horn (X:16, Y:37)"),
        new(49088, "Occult Mighty Guard", 2, "Crescent Bibliotaph", "North Horn (X:38, Y:31)"),
        new(49091, "Occult Aero III", 3, "Alabaster Blade", "Quarried Away (critical encounter)"),
        new(49090, "Occult White Wind", 3, "Crescent Flame", "North Horn (X:5, Y:36)"),
    ];

    /// <summary>The source for an action id, or null when it is not a Blue Mage spell.</summary>
    public static PhantomBlueMageSource? For(uint actionId)
    {
        foreach (var s in All)
        {
            if (s.ActionId == actionId)
                return s;
        }

        return null;
    }
}
