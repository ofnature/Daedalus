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
