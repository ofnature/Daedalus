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

    ProteusStatusHelper StatusHelper { get; }
    CasterPartyHelper PartyHelper { get; }
    ProteusDebugState Debug { get; }
    ITrainingService? TrainingService { get; }
}
