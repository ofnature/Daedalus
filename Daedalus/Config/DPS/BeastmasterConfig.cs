namespace Daedalus.Config.DPS;

/// <summary>
/// Beastmaster (Artemis) configuration.
/// <para>
/// Deliberately small. Beastmaster is a limited job with a Lv50 cap and no role actions, so there
/// is no burst window, no party coordination and no mitigation to configure — see
/// <see cref="Daedalus.Rotation.Common.Helpers.LimitedJobContentPolicy"/>.
/// </para>
/// </summary>
public sealed class BeastmasterConfig
{
    /// <summary>
    /// Fire the four instinctual skills (Avalanche / Mistral / Spinning / Gale Axe).
    /// They spend the whole TP gauge and run on their own 5s recast, independent of the GCD.
    /// </summary>
    public bool EnableInstinctualSkills { get; set; } = true;

    /// <summary>
    /// Prefer the affinity that completes an intentional combo (Sunstrider / Moonstalker) over
    /// simply firing whichever instinctual skill is off cooldown.
    /// </summary>
    public bool PreferIntentionalCombos { get; set; } = true;

    /// <summary>Order the familiar's instinctual skill with Trick when it is ready.</summary>
    public bool EnableTrick { get; set; } = true;

    /// <summary>
    /// Fire Parting Blow. Off by default: it deals 1,000 potency in 8y but the familiar RETREATS
    /// afterwards, which ends Trick until the player summons again. That is a trade the player
    /// should opt into, not one the rotation should make unprompted.
    /// </summary>
    public bool EnablePartingBlow { get; set; }

    #region Capture

    /// <summary>
    /// <b>Part 1 — scan logging (debug builds only).</b> Records the Gauge action's scan text into
    /// the beast-capture ledger. The tooling behind this is compiled out of Release entirely, so
    /// the toggle does nothing there; it exists so data collection can run with auto-capture OFF
    /// while samples are still being gathered.
    /// </summary>
    public bool EnableScanLogging { get; set; } = true;

    /// <summary>
    /// <b>Part 2 — auto-capture (all builds).</b> Fires Capture against beasts the ledger already
    /// knows are capturable. Independent of <see cref="EnableScanLogging"/> by design: collection
    /// and acting-on-collected-data are separate decisions, and running both at once while the
    /// scan wording is still unconfirmed is the case worth being able to avoid.
    /// </summary>
    public bool EnableAutoCapture { get; set; }

    /// <summary>
    /// How close to the beast's estimated death to apply Capture, in seconds. Lower wastes less of
    /// the 120s window; higher is safer against a time-to-kill estimate that runs long. Hard-clamped
    /// at runtime to the debuff window minus <see cref="CaptureSafetyMarginSeconds"/>, so no value
    /// here can cause the debuff to expire before the kill.
    /// </summary>
    public float CaptureLeadSeconds { get; set; } = 20f;

    /// <summary>
    /// Slack kept between the end of the Capture debuff and the expected kill, in seconds. Guards
    /// against a TTK estimate that runs low. Better slightly early than a missed window.
    /// </summary>
    public float CaptureSafetyMarginSeconds { get; set; } = 10f;

    #endregion
}
