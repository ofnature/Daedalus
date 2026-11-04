using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.Common;

namespace Daedalus.Rotation.ArtemisCore.Context;

/// <summary>
/// Beastmaster-specific rotation context.
/// <para>
/// Thin compared with every other job: Beastmaster has no role actions and no burst window to
/// coordinate. Its job gauge only became readable with ClientStructs 78975d83 (Dalamud 15.0.3.7), so
/// <see cref="Gauge"/> is nullable and everything that reads it keeps a path for when it is not there.
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

    /// <summary>
    /// No other Battlehorn is ready to replace the familiar that is out, so a retreat would leave the beastmaster alone.
    /// See <see cref="Helpers.ArtemisFamiliarKeeper.NoReplacementHorn"/>.
    /// </summary>
    bool NoReplacementHorn => false;

    /// <summary>
    /// A Wespe sits in a ready Battlehorn other than the one out: its Final Sting is still to come. See
    /// <see cref="Helpers.ArtemisFamiliarKeeper.WespeWaiting"/>.
    /// </summary>
    bool WespeWaiting => false;

    /// <summary>A Wespe sits in a horn other than the out one, ready or not (the Mantis holds its Soundwave for it).</summary>
    bool WespeInRoster => false;

    /// <summary>
    /// The job gauge this frame — TP, familiar TP, the instinct diamonds, what is lit on the Compass.
    /// Null when it could not be read; callers then fall back to inference.
    /// </summary>
    ArtemisGauge? Gauge { get; }

    /// <summary>The Trick → clockwise-axe pairing in progress.</summary>
    ArtemisTrickPairing TrickPairing { get; }

    /// <summary>Debug readout.</summary>
    ArtemisDebugState Debug { get; }
}
