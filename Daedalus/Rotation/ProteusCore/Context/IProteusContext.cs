using Daedalus.Config.DPS;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.ProteusCore.Helpers;
using Daedalus.Services.Training;

namespace Daedalus.Rotation.ProteusCore.Context;

/// <summary>
/// Blue Mage rotation context. No job gauge — BLU state is MP + statuses + the configured role.
/// </summary>
public interface IProteusContext : ICasterDpsRotationContext
{
    /// <summary>The configured role (the dropdown) — drives module behavior and mimicry.</summary>
    BluRole Role { get; }

    /// <summary>Mighty Guard stance active.</summary>
    bool HasMightyGuard { get; }

    /// <summary>Diamondback active (locked in the shell — do nothing else).</summary>
    bool HasDiamondback { get; }

    /// <summary>Waxing Nocturne — the Moon Flute +50% window is live.</summary>
    bool HasWaxingNocturne { get; }

    /// <summary>Waning Nocturne (Moon Flute hangover) — TOTAL action lockout, idle through it.</summary>
    bool HasWaningNocturne { get; }

    /// <summary>Bristle's +50% snapshot armed — the next damage push must be the DoT it was cast for.</summary>
    bool HasBoost { get; }

    /// <summary>Touch of Frost active — White Death is the filler until it drops.</summary>
    bool HasTouchOfFrost { get; }

    /// <summary>Mid-Surpanakha dump (fury stack live) — anything else drops the stack.</summary>
    bool HasSurpanakhasFury { get; }

    /// <summary>Basic Instinct's solo +100% buff is active.</summary>
    bool HasBasicInstinctBuff { get; }

    /// <summary>Toad Oil evasion buff active.</summary>
    bool HasToadOil { get; }

    /// <summary>Own Gobskin barrier still absorbing.</summary>
    bool HasGobskin { get; }

    /// <summary>Aetheric Mimicry: Healer is active (Pom Cure/Gobskin/Exuviation potency gate).</summary>
    bool HasHealerMimicry { get; }

    /// <summary>The mimicry matching <see cref="Role"/> is active.</summary>
    bool HasCorrectMimicry { get; }

    /// <summary>Any mimicry buff is active (wrong-role detection when Role changed).</summary>
    bool HasAnyMimicry { get; }

    /// <summary>Active spell-set reader; null when unavailable (gating degrades to learned-only).</summary>
    Daedalus.Services.Action.IBluLoadoutService? LoadoutService { get; }

    /// <summary>
    /// THE Blue Mage availability check: learned AND in the active 24-slot set. A learned
    /// spell outside the set cannot be cast. Fail-open to learned-only when slot data is
    /// unavailable.
    /// </summary>
    bool IsSpellUsable(uint actionId);

    ProteusStatusHelper StatusHelper { get; }
    CasterPartyHelper PartyHelper { get; }
    ProteusDebugState Debug { get; }
    ITrainingService? TrainingService { get; }
}
