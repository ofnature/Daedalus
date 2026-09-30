using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.Common;

namespace Daedalus.Rotation.ArtemisCore.Context;

/// <summary>
/// Beastmaster-specific rotation context.
/// <para>
/// Conspicuously thin compared with every other job, and that is accurate rather than unfinished:
/// Beastmaster has no readable job gauge (it is absent from ClientStructs), no role actions, and no
/// burst window to coordinate. What the rotation actually reasons about is the instinct chain and
/// whether the game will let an action fire.
/// </para>
/// </summary>
public interface IArtemisContext : IMeleeDpsRotationContext
{
    /// <summary>The instinctual combo clock — which affinity was last spent and what chains next.</summary>
    ArtemisInstinctTracker Instinct { get; }

    /// <summary>Whether a familiar is currently out, so the familiar orders are worth pushing.</summary>
    bool HasFamiliar { get; }

    /// <summary>
    /// Which beast is out (by name, else by the Battlehorn last pressed); null when no familiar is out
    /// or it cannot be identified. Its Trick colour decides the axe that completes the combo.
    /// </summary>
    Daedalus.Data.BstFamiliar? FamiliarBeast { get; }

    /// <summary>The Trick → clockwise-axe pairing in progress.</summary>
    ArtemisTrickPairing TrickPairing { get; }

    /// <summary>Debug readout.</summary>
    ArtemisDebugState Debug { get; }
}
