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
    byte Progress,
    int StartTimestamp = 0)
{
    /// <summary>
    /// Longest countdown we will believe. A CE registration window is a minute or two and a
    /// battle at most half an hour; anything past an hour means the value is not a countdown and
    /// showing it would be worse than showing nothing.
    /// </summary>
    internal const uint MaxPlausibleSeconds = 3600;

    /// <summary>
    /// The countdown to display, from the two sources the packet offers.
    ///
    /// <para>
    /// <c>SecondsLeft</c> is authoritative when populated — but it reads <b>0 during Register</b>
    /// (field 2026-08-08), which is exactly the stage where the number matters most: "JOIN NOW"
    /// with no time is missing the one fact that decides whether you can make it. The
    /// registration and warmup durations that would give it directly are <c>private</c> in
    /// ClientStructs and unreachable.
    /// </para>
    ///
    /// <para>
    /// <b><c>StartTimestamp</c> is the moment the BATTLE begins</b> — field-confirmed 2026-08-08
    /// ("Company of Stone — JOIN NOW 0:54"), so during Register the gap to now is precisely the
    /// time left to enter. That is what this returns when <c>SecondsLeft</c> is empty.
    /// </para>
    ///
    /// <para>
    /// The negative and implausible guards below are kept even though the semantics are now
    /// known: they are what makes a future patch that repurposes the field degrade to "no timer"
    /// instead of a confidently wrong countdown on the most time-critical line in the window.
    /// </para>
    /// </summary>
    internal static uint ResolveSecondsLeft(uint secondsLeft, int startTimestamp, long nowUnix)
    {
        if (secondsLeft > 0)
            return secondsLeft <= MaxPlausibleSeconds ? secondsLeft : 0;

        if (startTimestamp <= 0)
            return 0;

        var untilStart = startTimestamp - nowUnix;
        return untilStart > 0 && untilStart <= MaxPlausibleSeconds ? (uint)untilStart : 0;
    }

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
