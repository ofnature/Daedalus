namespace Daedalus.Services.Occult;

/// <summary>
/// Which stage of its lifecycle a Critical Encounter is in. Mirrors the game's own
/// <c>DynamicEventState</c> byte values exactly — do not renumber.
///
/// <para>
/// This is the difference between "a CE exists" and "you can still get into it". The window used
/// to collapse all three live stages into one line, so a CE that had already sealed read the same
/// as one you could still run to.
/// </para>
/// </summary>
public enum CriticalEncounterStage : byte
{
    /// <summary>Not running. Never surfaced.</summary>
    Inactive = 0,

    /// <summary>
    /// The join window: the announcement is up and the timer is counting down for people to
    /// enter the arena. This is the ONLY stage you can still join, so it is the one worth
    /// interrupting the player for.
    /// </summary>
    Register = 1,

    /// <summary>Registration has closed and the fight is about to start. Too late to enter.</summary>
    Warmup = 2,

    /// <summary>Underway. Cannot be joined.</summary>
    Battle = 3,
}

/// <summary>
/// A live Critical Encounter with the state the game actually tracks for it, rather than just
/// its name. Read from the Occult director's dynamic-event container.
/// </summary>
/// <param name="Name">Encounter name as the game displays it.</param>
/// <param name="Stage">Lifecycle stage — see <see cref="CriticalEncounterStage"/>.</param>
/// <param name="SecondsLeft">
/// Seconds remaining in the CURRENT stage. During <see cref="CriticalEncounterStage.Register"/>
/// that is how long you have to get in; during <see cref="CriticalEncounterStage.Battle"/> it is
/// the time limit remaining.
/// </param>
/// <param name="Participants">Players signed up / inside.</param>
/// <param name="MaxParticipants">Cap for the encounter (0 when the game did not report one).</param>
/// <param name="Progress">The director's progress byte for the fight; 0 outside Battle.</param>
public readonly record struct CriticalEncounterState(
    string Name,
    CriticalEncounterStage Stage,
    uint SecondsLeft,
    byte Participants,
    byte MaxParticipants,
    byte Progress)
{
    /// <summary>You can still enter this one. The whole point of tracking the stage.</summary>
    public bool CanJoin => Stage == CriticalEncounterStage.Register;

    /// <summary>Running or about to — visible, but the door is shut.</summary>
    public bool IsSealed => Stage is CriticalEncounterStage.Warmup or CriticalEncounterStage.Battle;

    /// <summary>Short human label for the stage, for windows and log lines.</summary>
    public string StageLabel => Stage switch
    {
        CriticalEncounterStage.Register => "JOIN NOW",
        CriticalEncounterStage.Warmup => "starting",
        CriticalEncounterStage.Battle => "in progress",
        _ => "inactive",
    };

    /// <summary>m:ss of <see cref="SecondsLeft"/>, or empty when the game reported no timer.</summary>
    public string TimeLeftLabel => SecondsLeft == 0
        ? string.Empty
        : $"{SecondsLeft / 60}:{SecondsLeft % 60:D2}";

    /// <summary>"12/32" when a cap is known, "12" when it is not, empty when nobody is in yet.</summary>
    public string ParticipantsLabel => Participants == 0 && MaxParticipants == 0
        ? string.Empty
        : MaxParticipants > 0
            ? $"{Participants}/{MaxParticipants}"
            : Participants.ToString();
}
