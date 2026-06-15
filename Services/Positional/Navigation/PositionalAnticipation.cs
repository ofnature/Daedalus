using Olympus.Services.Positional;

namespace Olympus.Services.Positional.Navigation;

/// <summary>Why a positional finisher is anticipated on the next GCD.</summary>
public enum PositionalAnticipationReason
{
    ComboSetup,
    MeikyoSen,
}

/// <summary>
/// Predicted positional requirement ~one GCD ahead of the finisher.
/// </summary>
public readonly record struct PositionalAnticipation(
    PositionalType Required,
    uint UpcomingFinisherActionId,
    PositionalAnticipationReason Reason);

/// <summary>
/// Job-neutral inputs for anticipation providers (SAM, NIN, …).
/// </summary>
public readonly record struct PositionalAnticipationContext(
    uint LastComboAction,
    int PlayerLevel,
    bool HasTrueNorth,
    bool TargetHasPositionalImmunity,
    bool IsAtRear,
    bool IsAtFlank,
    bool HasMeikyoShisui = false,
    bool HasGetsuSen = false,
    bool HasKaSen = false,
    bool HasSetsuSen = false,
    /// <summary>When true, skip Meikyo Sen-based anticipation (Avarice profile.Meikyo).</summary>
    bool SuppressMeikyoAnticipation = false,
    bool HasFugetsu = false,
    float FugetsuRemainingSeconds = 0f,
    bool HasFuka = false,
    float FukaRemainingSeconds = 0f);

/// <summary>
/// Job-specific logic for when a positional finisher is imminent.
/// </summary>
public interface IPositionalAnticipationProvider
{
    PositionalAnticipation? GetAnticipatedPositional(in PositionalAnticipationContext context);
}
