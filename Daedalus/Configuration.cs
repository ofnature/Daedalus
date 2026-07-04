using System;
using Dalamud.Configuration;
using Daedalus.Config;
using Daedalus.Config.DPS;
using Daedalus.Services.Targeting;

namespace Daedalus;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    // Runtime state
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Transient combat override driven by external automation plugins: Questionable through the
    /// RSR-compat IPC (<c>RotationSolverReborn.ChangeOperatingMode</c>, on at kill-step start / off
    /// at fight end), Henchman through the <c>Henchman.IsBusy</c> poll, and AutoDuty through
    /// <c>AutoDuty.IsStopped</c>. Never persisted — a crash mid-task must not leave the rotation
    /// permanently enabled, and the user's master switch stays untouched.
    /// Backed by process-wide state, NOT instance state: rotations run on a snapshot copy of this
    /// object (<see cref="Services.Content.DutyConfigurationService.RotationConfiguration"/>) that
    /// only refreshes on zone/settings changes — an instance flag written by the IPC bridges on the
    /// saved config would never reach the rotation mid-task.
    /// </summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool ExternalCombatOverride
    {
        get => ExternalCombatOverrideState.Active;
        set => ExternalCombatOverrideState.Active = value;
    }

    /// <summary>Rotation runs when the user's master switch OR the external-combat IPC override is on.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public bool EffectiveEnabled => Enabled || ExternalCombatOverride;

    /// <summary>Automation plugin currently holding the override ("Henchman", "AutoDuty", "Quest"); "" when none.</summary>
    [Newtonsoft.Json.JsonIgnore]
    public string ExternalCombatOverrideSource =>
        ExternalCombatOverrideState.Active ? ExternalCombatOverrideState.Source : "";

    public bool MainWindowVisible { get; set; } = true;
    public bool IsDebugWindowOpen { get; set; } = false;

    /// <summary>
    /// When true, pressing Escape will not close the Daedalus main window.
    /// </summary>
    public bool PreventEscapeClose { get; set; } = false;

    /// <summary>
    /// When true, Daedalus windows remain visible during in-game cutscenes.
    /// Default false (windows hide during cutscenes).
    /// </summary>
    public bool ShowDuringCutscenes { get; set; } = false;

    // Community & telemetry
    public bool HasSeenWelcome { get; set; } = false;
    public bool TelemetryEnabled { get; set; } = true;
    public string TelemetryEndpoint { get; set; } = "https://daedalus-telemetry.christopherscottkeller.workers.dev/";

    /// <summary>
    /// Optional language override. When set, uses this language instead of the game client language.
    /// Valid values: "en", "ja", "de", "fr", "zh", "tw", "ko", "es", "pt", "ru"
    /// Empty string or null means use game client language.
    /// </summary>
    public string LanguageOverride { get; set; } = string.Empty;

    /// <summary>
    /// When true, automatically applies dungeon/raid/trial tuning based on the current zone.
    /// Does not modify saved settings; rotations use a runtime overlay snapshot.
    /// </summary>
    public bool EnableAutoDutyConfig { get; set; } = true;

    /// <summary>
    /// Master kill-switch for all vNav-driven auto movement (positional reposition to flank/rear,
    /// burst melee approach, etc.). When false, no job will path the character around. Movement is
    /// also automatically suppressed when solo (no party), regardless of this flag. Default true.
    /// </summary>
    public bool EnableAutoMovement { get; set; } = true;

    /// <summary>
    /// For all melee jobs: when hugging the target, step straight back out to the outer melee edge
    /// ("max melee"). Pure range-keeping — not the run-around-the-mob positional dance — so unlike
    /// positional/burst movement it also runs solo (gated only by <see cref="EnableAutoMovement"/>).
    /// Default true.
    /// </summary>
    public bool MaintainMaxMelee { get; set; } = true;

    /// <summary>
    /// Global navigation / vNav tuning (vNav Flex grace band, solo lock, debug rings, tank stubs),
    /// surfaced in the Nav Control window. Not per-job.
    /// </summary>
    public NavConfig Nav { get; set; } = new();

    /// <summary>
    /// The currently active configuration preset.
    /// Set to Custom when user modifies individual settings after applying a preset.
    /// </summary>
    public ConfigurationPreset ActivePreset { get; set; } = ConfigurationPreset.Custom;

    // General behavior
    /// <summary>
    /// How long to wait after movement stops before casting (in seconds).
    /// Higher values = more conservative (safer), lower = more aggressive (faster DPS).
    /// Valid range: 0.0 to 2.0 seconds.
    /// </summary>
    private float _movementTolerance = 0.1f;
    public float MovementTolerance
    {
        get => _movementTolerance;
        set => _movementTolerance = Math.Clamp(value, 0.0f, 2.0f);
    }

    /// <summary>
    /// When true, Daedalus will start executing the rotation when auto-attack is active
    /// on the target, even before the InCombat flag is set.
    /// </summary>
    public bool EnableOnAutoAttack { get; set; } = false;

    /// <summary>
    /// When true, treat the rotation as in-combat while any party member or Trust ally
    /// is fighting, even before the local player has the InCombat flag.
    /// Pair with Tank Assist targeting for automatic assist on the pull.
    /// </summary>
    public bool EnableOnPartyInCombat { get; set; } = true;

    /// <summary>
    /// Suppress the forced auto-face setting while a nearby enemy is casting a look-away/gaze action
    /// (<see cref="Daedalus.Data.FFXIVConstants.GazeCastActionIds"/>), so the bot's casts don't turn the
    /// character into the gaze. Default true. (Gaze action list is curated/seeded as encountered.)
    /// </summary>
    public bool EnableLookAwaySafety { get; set; } = true;

    // Master category toggles
    public bool EnableHealing { get; set; } = true;
    public bool EnableDamage { get; set; } = true;
    public bool EnableDoT { get; set; } = true;

    // Nested configuration groups
    public HealingConfig Healing { get; set; } = new();
    public TimelineConfig Timeline { get; set; } = new();
    public DamageConfig Damage { get; set; } = new();
    public DotConfig Dot { get; set; } = new();
    public DefensiveConfig Defensive { get; set; } = new();
    public BuffConfig Buffs { get; set; } = new();
    public ResurrectionConfig Resurrection { get; set; } = new();
    public TargetingConfig Targeting { get; set; } = new();
    public RaidConfig Raid { get; set; } = new();
    public RoleActionConfig RoleActions { get; set; } = new();
    public DebugConfig Debug { get; set; } = new();
    public CalibrationConfig Calibration { get; set; } = new();
    public AnalyticsConfig Analytics { get; set; } = new();
    public FFlogsConfig FFLogs { get; set; } = new();
    public TrainingConfig Training { get; set; } = new();
    public OverlayConfig Overlay { get; set; } = new();
    public ParserConfig Parser { get; set; } = new();
    public ActionFeedConfig ActionFeed { get; set; } = new();
    public DrawHelperConfig DrawHelper { get; set; } = new();
    public InputConfig Input { get; set; } = new();

    // Job-specific configuration - Healers
    public HealerSharedConfig HealerShared { get; set; } = new();
    public ScholarConfig Scholar { get; set; } = new();

    /// <summary>Farm mode (kill mobs at spots until X of an item is in the bag). See docs/farm-mode.md.</summary>
    public FarmConfig Farm { get; set; } = new();
    public AstrologianConfig Astrologian { get; set; } = new();
    public SageConfig Sage { get; set; } = new();

    // Party coordination (multi-Daedalus IPC)
    public PartyCoordinationConfig PartyCoordination { get; set; } = new();

    // Consumable automation (combat tinctures)
    public ConsumablesConfig Consumables { get; set; } = new();

    // Role-specific configuration - Tanks
    public TankConfig Tank { get; set; } = new();

    // Job-specific configuration - Melee DPS
    public MeleeSharedConfig MeleeShared { get; set; } = new();
    public DragoonConfig Dragoon { get; set; } = new();
    public NinjaConfig Ninja { get; set; } = new();
    public SamuraiConfig Samurai { get; set; } = new();
    public MonkConfig Monk { get; set; } = new();
    public ReaperConfig Reaper { get; set; } = new();
    public ViperConfig Viper { get; set; } = new();

    // Job-specific configuration - Ranged Physical DPS
    public RangedSharedConfig RangedShared { get; set; } = new();
    public MachinistConfig Machinist { get; set; } = new();
    public BardConfig Bard { get; set; } = new();
    public DancerConfig Dancer { get; set; } = new();

    // Job-specific configuration - Casters
    public CasterSharedConfig CasterShared { get; set; } = new();
    public BlackMageConfig BlackMage { get; set; } = new();

    /// <summary>Blue Mage (Proteus) settings — role dropdown, mimicry, kit toggles.</summary>
    public BlueMageConfig BlueMage { get; set; } = new();
    public SummonerConfig Summoner { get; set; } = new();
    public RedMageConfig RedMage { get; set; } = new();
    public PictomancerConfig Pictomancer { get; set; } = new();

    /// <summary>
    /// Resets all configuration values to their defaults.
    /// Preserves Enabled state and window visibility settings.
    /// </summary>
    public void ResetToDefaults()
    {
        // Preserve runtime state
        var wasEnabled = Enabled;
        var mainVisible = MainWindowVisible;
        var analyticsVisible = Analytics.AnalyticsWindowVisible;
        var trainingVisible = Training.TrainingWindowVisible;
        var seenWelcome = HasSeenWelcome;
        var languageOverride = LanguageOverride;
        var preventEscapeClose = PreventEscapeClose;
        var showDuringCutscenes = ShowDuringCutscenes;

        // Reset general behavior
        EnableOnAutoAttack = false;
        EnableOnPartyInCombat = true;
        MovementTolerance = 0.1f;

        // Reset master toggles
        EnableHealing = true;
        EnableDamage = true;
        EnableDoT = true;

        // Reset all nested configs to fresh instances
        Healing = new HealingConfig();
        Timeline = new TimelineConfig();
        Damage = new DamageConfig();
        Dot = new DotConfig();
        Defensive = new DefensiveConfig();
        Buffs = new BuffConfig();
        Resurrection = new ResurrectionConfig();
        Targeting = new TargetingConfig();
        Nav = new NavConfig();
        RoleActions = new RoleActionConfig();
        Debug = new DebugConfig();
        Calibration = new CalibrationConfig();
        Analytics = new AnalyticsConfig();
        FFLogs = new FFlogsConfig();
        Training = new TrainingConfig();
        Overlay = new OverlayConfig();
        ActionFeed = new ActionFeedConfig();
        DrawHelper = new DrawHelperConfig();
        HealerShared = new HealerSharedConfig();
        Scholar = new ScholarConfig();
        Astrologian = new AstrologianConfig();
        Sage = new SageConfig();
        Tank = new TankConfig();
        PartyCoordination = new PartyCoordinationConfig();

        // Reset DPS configs
        MeleeShared = new MeleeSharedConfig();
        Dragoon = new DragoonConfig();
        Ninja = new NinjaConfig();
        Samurai = new SamuraiConfig();
        Monk = new MonkConfig();
        Reaper = new ReaperConfig();
        Viper = new ViperConfig();
        RangedShared = new RangedSharedConfig();
        Machinist = new MachinistConfig();
        Bard = new BardConfig();
        Dancer = new DancerConfig();
        CasterShared = new CasterSharedConfig();
        BlackMage = new BlackMageConfig();
        Summoner = new SummonerConfig();
        RedMage = new RedMageConfig();
        Pictomancer = new PictomancerConfig();

        // Reset telemetry to defaults
        TelemetryEnabled = true;
        TelemetryEndpoint = "https://daedalus-telemetry.christopherscottkeller.workers.dev/";

        // Reset preset to Custom so the UI does not show a stale preset name
        ActivePreset = ConfigurationPreset.Custom;

        // Restore preserved values
        Enabled = wasEnabled;
        MainWindowVisible = mainVisible;
        Analytics.AnalyticsWindowVisible = analyticsVisible;
        Training.TrainingWindowVisible = trainingVisible;
        HasSeenWelcome = seenWelcome;
        LanguageOverride = languageOverride;
        PreventEscapeClose = preventEscapeClose;
        ShowDuringCutscenes = showDuringCutscenes;
    }
}

/// <summary>
/// Process-wide holder for the transient external-automation combat override. See
/// <see cref="Configuration.ExternalCombatOverride"/> for why this must not be instance state.
/// Framework-thread access only (IPC callbacks and bridge polls both run there).
/// </summary>
internal static class ExternalCombatOverrideState
{
    internal static bool Active;

    /// <summary>Which automation plugin currently holds the override ("Henchman", "AutoDuty", "Quest"); "" when none.</summary>
    internal static string Source = "";
}
