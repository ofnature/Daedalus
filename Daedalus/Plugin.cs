using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Ipc;
using Daedalus.Rotation;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Calculation;
using Daedalus.Services.Combat;
using Daedalus.Services.Cooldown;
using Daedalus.Services.Debuff;
using Daedalus.Services.Debug;
using Daedalus.Services.Healing;
using Daedalus.Services.Input;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Scholar;
using Daedalus.Services.Cache;
using Daedalus.Services.Tank;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;
using Daedalus.Services.Analytics;
using Daedalus.Services.FFLogs;
using Daedalus.Services.Training;
using Daedalus.Timeline;
using Daedalus.Training;
using Daedalus.Localization;
using Daedalus.Services.Drawing;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Windows;
using Daedalus.Windows.Debug.Tabs;
using Daedalus.Windows.Training;

namespace Daedalus;

public sealed class Plugin : IDalamudPlugin
{
    public const string PluginVersion = "0.1.15";
    private const string CommandName = "/daedalus";
    private const string CommandAlias = "/dae";

    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IFramework framework;
    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly IPluginLog log;
    private readonly IClientState clientState;
    private readonly ICommandManager commandManager;
    private readonly IChatGui chatGui;
    private readonly IDataManager dataManager;
    private readonly ICondition condition;
    private readonly IGameConfig gameConfig;
    private readonly IJobGauges jobGauges;
    private readonly ITargetManager targetManager;

    // Auto-face: original AutoFaceTargetOnAction value captured before we override it (restored on unload).
    private bool? _originalAutoFaceTarget;

    private readonly Configuration configuration;
    private readonly ActionTracker actionTracker;
    private readonly CombatEventService combatEventService;
    private readonly DamageIntakeService damageIntakeService;
    private readonly DoTTrackingService dotTrackingService;
    private readonly HealingIntakeService healingIntakeService;
    private readonly DamageTrendService damageTrendService;
    private readonly CooldownPlanner cooldownPlanner;
    private readonly TargetingService targetingService;
    private readonly GapCloserSafetyService gapCloserSafetyService;
    private readonly TimeToKillService timeToKillService;
    private readonly ShieldTrackingService shieldTrackingService;
    private readonly HpPredictionService hpPredictionService;
    private readonly ActionService actionService;
    private readonly Daedalus.Services.Debug.DebugLogService debugLogService;
    private readonly PlayerStatsService playerStatsService;
    private readonly HealingSpellSelector healingSpellSelector;
    private readonly SpellStatusService spellStatusService;
    private readonly DebugService debugService;
    private readonly DebuffDetectionService debuffDetectionService;
    private readonly RotationManager rotationManager;
    private readonly ServiceContainer serviceContainer;
    private readonly RotationFactory rotationFactory;

    // Tank services
    private readonly EnmityService enmityService;
    private readonly TankCooldownService tankCooldownService;

    // Melee DPS services
    private readonly PositionalService positionalService;
    private readonly VNavService vNavService;
    private readonly MovementArbiter movementArbiter;
    private readonly BossModSafetyService bossModSafetyService;
    private readonly BossModForecastService bossModForecastService;
    private readonly PositionalMovementService positionalMovementService;
    private readonly BmrAiConfigService bmrAiConfigService;
    private readonly SamuraiPositionalAnticipationProvider samuraiPositionalAnticipationProvider;
    private readonly NinjaPositionalAnticipationProvider ninjaPositionalAnticipationProvider;

    // Burst window tracking for DPS rotations
    private readonly BurstWindowService burstWindowService;

    // Player-intent overrides (Shift = burst now, Ctrl = conservative)
    private readonly ModifierKeyService modifierKeyService;

    // Timeline service
    private readonly TimelineService timelineService;

    // Party coordination (multi-Daedalus IPC)
    private readonly PartyCoordinationService? partyCoordinationService;
    private readonly PartyCoordinationIpc? partyCoordinationIpc;
    private readonly Daedalus.Services.Network.LanCoordinator? lanCoordinator;
    private readonly Daedalus.Services.Network.CoordinationBus? coordinationBus;
    private readonly Daedalus.Services.Targeting.PartyTargetingCoordinator? partyTargetingCoordinator;
    private readonly LanPartyWindow? lanPartyWindow;

    // Performance analytics
    private readonly PerformanceTracker performanceTracker;
    private readonly DpsMeterService dpsMeterService;
    private readonly BluLoadoutService bluLoadoutService;

    // FFLogs integration
    private readonly FFlogsService? fflogsService;

    // Post-combat coaching summaries
    private readonly FightSummaryService? fightSummaryService;

    // Training mode
    private readonly TrainingDataRegistry trainingDataRegistry;
    private readonly TrainingService trainingService;
    private readonly RealTimeCoachingService realTimeCoachingService;
    private readonly DecisionValidationService decisionValidationService;
    private readonly SpacedRepetitionService spacedRepetitionService;

    // Localization
    private readonly DaedalusLocalization localization;
    private readonly GameDataLocalizer gameDataLocalizer;

    private readonly WindowSystem windowSystem = new("Daedalus");
    private readonly ConfigWindow configWindow;
    private readonly MainWindow mainWindow;
    private readonly ControlWindow controlWindow;
    private readonly NavControlWindow navControlWindow;
    private readonly RaidWindow raidWindow;
    private readonly MissingWindow missingWindow;
    private readonly DebugWindow debugWindow;
    private readonly WelcomeWindow welcomeWindow;
    private readonly AnalyticsWindow analyticsWindow;
    private readonly TrainingWindow trainingWindow;
    private readonly ChangelogWindow changelogWindow;
    private readonly HintOverlay hintOverlay;
    private readonly OverlayWindow overlayWindow;
    private readonly ActionFeedWindow actionFeedWindow;
    private readonly DpsMeterWindow dpsMeterWindow;
    private readonly TelemetryService telemetryService;
    private readonly DrawCanvas drawCanvas;
    private readonly DrawingService drawingService;
    private readonly AoETracker aoeTracker;
    private readonly SmartAoEService smartAoEService;

    private readonly DaedalusIpc DaedalusIpc;
    private readonly LanRosterIpc lanRosterIpc;
    private readonly Daedalus.Services.Party.PartyInviteAcceptService partyInviteAcceptService;
    private readonly RsrCompatIpc rsrCompatIpc;
    private readonly AutomationBusyBridge[] automationBridges;
    private readonly QuestionableIpc questionableIpc;
    private readonly Daedalus.Services.Farm.FarmModeService farmModeService;
    private readonly Daedalus.Services.Farm.GarlandDropSource garlandDropSource;
    private readonly FarmWindow farmWindow;
    private readonly UpdateCheckerService updateCheckerService;

    // Pull-intent state machine + consumable services (tincture automation)
    private readonly Daedalus.Services.Pull.PullIntentService pullIntentService;
    private readonly Daedalus.Services.Content.HighEndContentService highEndContentService;
    private readonly Daedalus.Services.Content.DutyContentService dutyContentService;
    private readonly Daedalus.Services.Content.DutyConfigurationService dutyConfigurationService;
    private readonly Daedalus.Services.Consumables.DalamudInventoryProbe inventoryProbe;
    private readonly Daedalus.Services.Consumables.DalamudTinctureCooldownProbe tinctureCooldownProbe;
    private readonly Daedalus.Services.Consumables.ConsumableService consumableService;
    private readonly Daedalus.Services.Consumables.TinctureDispatcher tinctureDispatcher;

    // Error metrics
    private readonly ErrorMetricsService errorMetricsService;

    // Stored event handler delegates to allow removal in Dispose
    private readonly Action<uint, uint> onAbilityUsedHandler;
    private readonly Action<FightSession> onSessionCompletedHandler;
    public Plugin(
        IDalamudPluginInterface pluginInterface,
        IFramework framework,
        IObjectTable objectTable,
        IPartyList partyList,
        IPluginLog log,
        IClientState clientState,
        ICommandManager commandManager,
        IChatGui chatGui,
        IDataManager dataManager,
        ICondition condition,
        IGameConfig gameConfig,
        IGameInteropProvider gameInteropProvider,
        ITargetManager targetManager,
        IJobGauges jobGauges,
        ITextureProvider textureProvider,
        IGameGui gameGui,
        INotificationManager notificationManager,
        IKeyState keyState)
    {
        this.pluginInterface = pluginInterface;
        this.framework = framework;
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.log = log;
        this.clientState = clientState;
        this.commandManager = commandManager;
        this.chatGui = chatGui;
        this.dataManager = dataManager;
        this.condition = condition;
        this.gameConfig = gameConfig;
        Rotation.Base.RotationServices.Condition = condition;
        this.jobGauges = jobGauges;
        this.targetManager = targetManager;

        this.configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // One-time migration for changed defaults (Version 2 -> 3). Seraph "on cooldown" was the
        // old shipped default that nobody chose deliberately — it burns a 120s healing cooldown at
        // full party HP seconds into every pull (caught in SCH field logs). Users who re-pick
        // OnCooldown after this migration keep their choice (Version stays 3).
        if (configuration.Version < 3)
        {
            if (configuration.Scholar.SeraphStrategy == Daedalus.Config.SeraphUsageStrategy.OnCooldown)
                configuration.Scholar.SeraphStrategy = Daedalus.Config.SeraphUsageStrategy.SaveForDamage;
            configuration.Version = 3;
            pluginInterface.SavePluginConfig(configuration);
        }

        // Initialize localization (must be early, before UI construction)
        this.localization = new DaedalusLocalization(clientState, configuration, log);
        this.gameDataLocalizer = new GameDataLocalizer(dataManager);

        // Load persisted calibration data for healing calculations
        HealingCalculator.LoadCalibration(configuration.Calibration);

        this.actionTracker = new ActionTracker(dataManager, configuration);
        this.combatEventService = new CombatEventService(gameInteropProvider, log, objectTable, configuration);
        this.damageIntakeService = new DamageIntakeService(combatEventService);
        this.dotTrackingService = new DoTTrackingService(combatEventService, damageIntakeService);
        this.healingIntakeService = new HealingIntakeService(combatEventService);
        this.damageTrendService = new DamageTrendService(damageIntakeService, healingIntakeService);
        this.cooldownPlanner = new CooldownPlanner(damageIntakeService, damageTrendService, configuration);
        this.gapCloserSafetyService = new GapCloserSafetyService(configuration, targetManager);

        // Content classifiers + the non-destructive runtime config overlay must be built before any
        // service that reads the effective (overlaid) config — TargetingService reads it so per-fight
        // strategy overrides (Raid window) and the auto-duty profile take effect. Primed for the
        // current zone in case the plugin loads while already in a duty, then refreshed once so the
        // overlay reflects that zone immediately.
        this.highEndContentService = new Daedalus.Services.Content.HighEndContentService(dataManager);
        this.highEndContentService.OnTerritoryChanged((ushort)clientState.TerritoryType);
        this.dutyContentService = new Daedalus.Services.Content.DutyContentService(dataManager);
        this.dutyConfigurationService = new Daedalus.Services.Content.DutyConfigurationService(configuration, dutyContentService);
        this.dutyContentService.OnTerritoryChanged(
            (ushort)clientState.TerritoryType,
            highEndContentService.IsHighEndZone,
            partyList.Length);
        this.dutyConfigurationService.Refresh();

        this.timeToKillService = new TimeToKillService();
        this.targetingService = new TargetingService(objectTable, partyList, targetManager, dutyConfigurationService.RotationConfiguration, gapCloserSafetyService, timeToKillService);
        this.shieldTrackingService = new ShieldTrackingService(objectTable, partyList, log);

        // New action system services
        this.hpPredictionService = new HpPredictionService(
            combatEventService,
            configuration,
            shieldTrackingService,
            damageTrendService);
        this.debugLogService = new Daedalus.Services.Debug.DebugLogService(
            configuration, pluginInterface.ConfigDirectory.FullName, log);
        combatEventService.AttachDebugLog(debugLogService);
        this.actionService = new ActionService(actionTracker, objectTable: objectTable, dataManager: dataManager,
            debugLog: debugLogService);
        this.playerStatsService = new PlayerStatsService(log, dataManager);

        // Healing spell selector (evaluates all heals and picks the best)
        this.healingSpellSelector = new HealingSpellSelector(
            actionService,
            playerStatsService,
            hpPredictionService,
            combatEventService,
            configuration,
            damageTrendService);

        // Spell status service (provides real-time status of all WHM spells)
        this.spellStatusService = new SpellStatusService(actionService);

        // Debuff detection service for Esuna
        this.debuffDetectionService = new DebuffDetectionService(dataManager);

        // Timeline service for fight-aware predictions (must precede TankCooldownService)
        this.timelineService = new TimelineService(log, combatEventService);
        this.onAbilityUsedHandler = (sourceId, actionId) => timelineService.OnAbilityUsed(sourceId, actionId);
        combatEventService.OnAbilityUsed += this.onAbilityUsedHandler;

        // Wire timeline into damage prediction service
        this.damageIntakeService.SetTimelineService(this.timelineService);

        // Tank services
        this.enmityService = new EnmityService(objectTable, partyList);
        this.tankCooldownService = new TankCooldownService(configuration.Tank, this.timelineService);

        // Melee DPS services
        this.positionalService = new PositionalService();
        this.vNavService = new VNavService(pluginInterface, log);
        this.bossModSafetyService = new BossModSafetyService(pluginInterface, log);
        this.movementArbiter = new MovementArbiter(
            vNavService, bossModSafetyService, () => configuration.Nav.YieldToBmrMovement,
            debugLog: debugLogService);
        Rotation.Base.RotationServices.VNav = movementArbiter;
        Rotation.Base.RotationServices.MovementArbiter = movementArbiter;
        this.bossModForecastService = new BossModForecastService(pluginInterface, log);
        this.positionalMovementService = new PositionalMovementService(movementArbiter, bossModSafetyService);
        this.bmrAiConfigService = new BmrAiConfigService(pluginInterface, bossModSafetyService, log, debugLogService);
        this.samuraiPositionalAnticipationProvider = new SamuraiPositionalAnticipationProvider();
        this.ninjaPositionalAnticipationProvider = new NinjaPositionalAnticipationProvider();

        // Party coordination service (multi-Daedalus IPC)
        if (configuration.PartyCoordination.EnablePartyCoordination)
        {
            this.partyCoordinationService = new PartyCoordinationService(configuration.PartyCoordination, log);
            this.partyCoordinationIpc = new PartyCoordinationIpc(pluginInterface, partyCoordinationService, log);
        }

        // LAN coordinator (cross-machine UDP broadcast on the local VLAN). Opt-in; same-machine
        // toons keep Dalamud IPC, the CoordinationBus mirrors + dedups between the transports.
        if (configuration.PartyCoordination.LanCoordinatorEnabled)
        {
            var machineId = configuration.PartyCoordination.GetOrCreateMachineId();
            pluginInterface.SavePluginConfig(configuration); // persist a freshly-generated machine id
            this.lanCoordinator = new Daedalus.Services.Network.LanCoordinator(
                log, machineId, configuration.PartyCoordination.LanPort);
            this.coordinationBus = new Daedalus.Services.Network.CoordinationBus(
                log, lanCoordinator, partyCoordinationService, machineId);
            this.coordinationBus.HeartbeatProvider = BuildLanHeartbeat;
            this.lanCoordinator.Start(); // bind failure logs + falls back to IPC-only (Status=Error)
            this.clientState.TerritoryChanged += OnLanTerritoryChanged;
            Windows.Config.Shared.PartyCoordinationSection.LanStatusSource = () =>
            {
                var status = lanCoordinator.Status.ToString();
                var detail = lanCoordinator.Status == Daedalus.Services.Network.LanStatus.Error
                    ? lanCoordinator.LastError
                    : $"{coordinationBus!.PeerToonCount} peer toon(s), {coordinationBus.PeerMachineCount} machine(s)";
                return (lanCoordinator.Status == Daedalus.Services.Network.LanStatus.Connected ? "Connected"
                        : lanCoordinator.Status == Daedalus.Services.Network.LanStatus.Error ? "Error"
                        : status, detail);
            };
        }

        // Burst window service (DPS resource pooling during raid buffs)
        // Created after partyCoordinationService so it can use IPC data when available.
        // Wired to CombatEventService for low-latency cast-event burst detection: when
        // a party member casts a known raid buff, the window opens immediately rather
        // than waiting for the per-frame status scan to see the buff land.
        this.burstWindowService = new BurstWindowService(
            this.partyCoordinationService,
            this.combatEventService,
            partyList,
            objectTable);

        // Modifier-key overrides (Shift = burst, Ctrl = conservative). Static accessor
        // on BurstHoldHelper means all 30+ pooling decision sites pick up the override
        // automatically without threading the service through every constructor.
        this.modifierKeyService = new ModifierKeyService(keyState, configuration);
        BurstHoldHelper.ModifierKeys = this.modifierKeyService;

        // Performance analytics
        this.performanceTracker = new PerformanceTracker(
            configuration.Analytics,
            actionTracker,
            combatEventService,
            objectTable,
            partyList,
            log,
            dataManager,
            partyCoordinationService,
            pluginInterface.ConfigDirectory.FullName);

        // BLU active spell-set reader (learned+slotted availability for Proteus + Missing window)
        this.bluLoadoutService = new BluLoadoutService(
            () => objectTable.LocalPlayer?.ClassJob.RowId ?? 0);

        // Full-party DPS parser; the bus (when LAN is enabled) adds cross-toon self-reporting
        this.dpsMeterService = new DpsMeterService(
            combatEventService, objectTable, configuration.Parser,
            partyList, () => (ushort)clientState.TerritoryType);
        if (this.coordinationBus != null)
            this.dpsMeterService.AttachCoordinationBus(this.coordinationBus);

        // Party-wide target-mode enforcement (Focus / Split / Kill Adds). Reads mode/focus/off-tank
        // from the bus; MT-invariant role gating lives in the coordinator. The overlay reapplies the
        // effective targeting config, re-run whenever the mode changes.
        if (this.coordinationBus != null)
        {
            this.partyTargetingCoordinator = new Daedalus.Services.Targeting.PartyTargetingCoordinator(
                this.coordinationBus, objectTable, targetManager, this.dpsMeterService);
            this.dutyConfigurationService.PartyModeOverlayProvider =
                this.partyTargetingCoordinator.BuildTargetingOverlay;
            this.coordinationBus.OnTargetModeChanged += () => this.dutyConfigurationService.Refresh();
            // Also refresh on eligibility flips (job change mid-mode, login) — mode alone isn't
            // enough to keep the overlay honest.
            this.partyTargetingCoordinator.OnEnforcementStateChanged += () => this.dutyConfigurationService.Refresh();
        }

        // FFLogs integration
        this.fflogsService = new FFlogsService(configuration.FFLogs, log);

        // Post-combat coaching summaries
        this.fightSummaryService = new FightSummaryService(
            performanceTracker, actionTracker, burstWindowService, fflogsService, configuration);

        // Training mode
        this.trainingDataRegistry = new TrainingDataRegistry(log);
        this.trainingService = new TrainingService(configuration.Training, objectTable, trainingDataRegistry, log);

        // Real-time coaching hints (v3.49.0)
        this.realTimeCoachingService = new RealTimeCoachingService(
            configuration.Training,
            trainingService,
            log);

        // Decision validation (v3.50.0)
        this.decisionValidationService = new DecisionValidationService(
            configuration.Training,
            log);

        // Spaced repetition (v3.52.0)
        this.spacedRepetitionService = new SpacedRepetitionService(
            configuration.Training,
            trainingService,
            log);

        // Connect spaced repetition to training service for retention tracking (v4.0.0)
        this.trainingService.SetSpacedRepetitionService(this.spacedRepetitionService);

        // Connect analytics to training recommendations (v3.10.0)
        this.onSessionCompletedHandler = session => this.trainingService.UpdateRecommendations(session);
        this.performanceTracker.OnSessionCompleted += this.onSessionCompletedHandler;

        // Error metrics service (aggregates suppressed errors for debugging)
        this.errorMetricsService = new ErrorMetricsService();

        // Pull-intent state machine. Driven each frame by Plugin.Update from
        // LocalPlayer.IsCasting + ActionManager.QueuedActionId + InCombat.
        this.pullIntentService = new Daedalus.Services.Pull.PullIntentService();

        // Inventory and tincture-cooldown probes (production-side wrappers).
        this.inventoryProbe = new Daedalus.Services.Consumables.DalamudInventoryProbe(errorMetricsService);
        this.tinctureCooldownProbe = new Daedalus.Services.Consumables.DalamudTinctureCooldownProbe(errorMetricsService);

        // Consumable service: inventory probing + recast cooldown + ShouldUseTinctureNow gate.
        // Per-fight inventory-empty warning routed through chatGui.
        this.consumableService = new Daedalus.Services.Consumables.ConsumableService(
            configuration.Consumables,
            pullIntentService,
            highEndContentService,
            inventoryProbe,
            tinctureCooldownProbe,
            chatGui);

        // Tincture dispatcher: shared by Path 1 (TinctureCandidate in PrePullModule)
        // and Path 2 (in-combat re-pot push from BaseRotation).
        this.tinctureDispatcher = new Daedalus.Services.Consumables.TinctureDispatcher(
            consumableService,
            burstWindowService,
            actionService,
            objectTable);

        // Smart AoE service (must be created before service container)
        this.aoeTracker = new AoETracker();
        this.smartAoEService = new SmartAoEService(targetingService, dataManager, aoeTracker, log);
        this.smartAoEService.SubscribeToCombatEvents(combatEventService);

        // Create service container for rotation dependency injection
        this.serviceContainer = CreateServiceContainer();

        // Create rotation manager and factory, then auto-discover rotations
        this.rotationManager = new RotationManager();
        this.rotationFactory = new RotationFactory(serviceContainer, log);
        var rotationCount = rotationFactory.DiscoverAndRegisterFactories(rotationManager);
        log.Information("Registered {Count} rotation modules via auto-discovery", rotationCount);

        // Debug service aggregates all debug data
        this.debugService = new DebugService(
            actionTracker,
            actionService,
            combatEventService,
            hpPredictionService,
            playerStatsService,
            healingSpellSelector,
            spellStatusService,
            rotationManager,
            targetingService,
            objectTable,
            dataManager,
            configuration,
            movementArbiter,
            debugLogService,
            movementArbiter);

        this.drawingService = new DrawingService(pluginInterface, configuration.DrawHelper, gameGui, log);
        this.drawCanvas = new DrawCanvas(drawingService, configuration, objectTable, clientState, targetManager, gameGui, positionalService, rotationManager, partyList);
        this.updateCheckerService = new UpdateCheckerService(PluginVersion, notificationManager, log);
        this.configWindow = new ConfigWindow(configuration, SaveConfiguration, updateCheckerService, textureProvider, dutyContentService);
        this.controlWindow = new ControlWindow(configuration, SaveConfiguration, rotationManager, textureProvider);
        this.navControlWindow = new NavControlWindow(configuration, SaveConfiguration, bmrAiConfigService, movementArbiter);
        this.raidWindow = new RaidWindow(configuration, SaveConfiguration, dutyContentService);
        this.missingWindow = new MissingWindow(debugService, bluLoadoutService);
        if (coordinationBus != null && lanCoordinator != null)
            this.lanPartyWindow = new LanPartyWindow(coordinationBus, lanCoordinator, configuration, SaveConfiguration, objectTable, targetManager);
        this.mainWindow = new MainWindow(configuration, SaveConfiguration, OpenConfigUI, OpenDebugUI, OpenAnalyticsUI, OpenTrainingUI, OpenChangelogUI, OpenOverlayUI, OpenControlUI, OpenNavControlUI, OpenRaidUI, OpenMissingUI, PluginVersion, rotationManager, textureProvider,
            actionTracker: actionTracker, dutyContent: dutyContentService);
        this.mainWindow.LanConnected = () => this.lanCoordinator?.Status == Daedalus.Services.Network.LanStatus.Connected;
        if (lanPartyWindow != null)
            this.mainWindow.OpenLanParty = () => lanPartyWindow.Toggle();
        var smartAoETab = new SmartAoETab(aoeTracker, drawCanvas, objectTable);
        this.debugWindow = new DebugWindow(debugService, configuration, timelineService, smartAoETab, debugLogService);
        this.welcomeWindow = new WelcomeWindow(configuration, SaveConfiguration, OpenConfigUI);
        this.analyticsWindow = new AnalyticsWindow(performanceTracker, configuration, SaveConfiguration, fflogsService, fightSummaryService);
        this.trainingWindow = new TrainingWindow(trainingService, configuration, decisionValidationService, spacedRepetitionService);
        this.changelogWindow = new ChangelogWindow();
        this.hintOverlay = new HintOverlay(realTimeCoachingService, configuration.Training);
        this.overlayWindow = new OverlayWindow(configuration, SaveConfiguration, rotationManager, partyList, this.timelineService, dutyContentService, bossModForecastService);
        this.actionFeedWindow = new ActionFeedWindow(configuration, SaveConfiguration, actionService, textureProvider);
        this.dpsMeterWindow = new DpsMeterWindow(configuration, SaveConfiguration, dpsMeterService);
        this.mainWindow.OpenParser = () => this.dpsMeterWindow.Toggle();
        this.mainWindow.ParserActive = () => this.dpsMeterService.Current != null;

        // Farm mode: Daedalus-driven grinding (kill profile mobs at spots until X items in bag).
        this.farmModeService = new Daedalus.Services.Farm.FarmModeService(
            configuration, objectTable, targetManager, targetingService, movementArbiter,
            inventoryProbe, clientState, log);
        this.farmModeService.Notify += message => chatGui.Print(message);
        this.garlandDropSource = new Daedalus.Services.Farm.GarlandDropSource(dataManager, log);
        this.farmWindow = new FarmWindow(farmModeService, garlandDropSource, dataManager, targetManager, clientState, objectTable);
        this.mainWindow.OpenFarm = () => this.farmWindow.Toggle();
        this.mainWindow.FarmActive = () => this.farmModeService.IsRunning;

        // Telemetry service for anonymous usage tracking
        this.telemetryService = new TelemetryService(configuration, log);

        // IPC interface for external plugin integration
        this.DaedalusIpc = new DaedalusIpc(
            pluginInterface,
            configuration,
            SaveConfiguration,
            log,
            PluginVersion,
            () => rotationManager);

        // LAN roster read-only IPC for companion plugins (Charon). Registered unconditionally —
        // returns "[]" while the LAN coordinator is disabled so consumers can tell "no roster"
        // apart from "Daedalus absent".
        this.lanRosterIpc = new LanRosterIpc(pluginInterface, () => coordinationBus, log);

        // Receive half of one-click grouping: auto-accept invites from rostered toons (opt-in).
        this.partyInviteAcceptService = new Daedalus.Services.Party.PartyInviteAcceptService(gameGui, log);

        // RSR-compat gates: lets Questionable's kill-quest combat module (configured to
        // "Rotation Solver Reborn") start/stop Daedalus around quest fights. Questionable
        // targets the kill mobs itself; we just run while the transient override is on.
        this.rsrCompatIpc = new RsrCompatIpc(pluginInterface, configuration, log);
        this.rsrCompatIpc.OverrideChanged += enabled =>
            chatGui.Print($"Daedalus rotation {(enabled ? "started" : "stopped")} by quest plugin.");

        // Automation bridges: Henchman and AutoDuty select their rotation plugin by installed
        // internal name, so the RSR-compat gates can't hook them — instead we run while their
        // state IPC reports a task active. Henchman covers overworld hunt farming (it targets
        // each mark itself); AutoDuty covers duty runs, including the dungeons Henchman delegates
        // to it for duty hunt-log marks.
        this.automationBridges =
        [
            new AutomationBusyBridge(pluginInterface, configuration, log, "Henchman", "Henchman.IsBusy", busyGateValue: true),
            new AutomationBusyBridge(pluginInterface, configuration, log, "AutoDuty", "AutoDuty.IsStopped", busyGateValue: false),
        ];
        foreach (var bridge in this.automationBridges)
        {
            var name = bridge.PluginName;
            bridge.OverrideChanged += enabled =>
                chatGui.Print(enabled
                    ? $"Daedalus rotation started — {name} is running."
                    : $"Daedalus rotation stopped — {name} finished.");
        }

        // Questionable kill-step bridge: without a combat module configured, Questionable never
        // targets kill mobs itself — this reads its step IPC and does Henchman-style targeting
        // (nearest attackable enemy) while a Combat step is active. Coexists with the RSR-compat
        // path when the user configures Questionable's combat module instead.
        this.questionableIpc = new QuestionableIpc(
            pluginInterface, configuration, log, targetManager, targetingService, objectTable);
        this.questionableIpc.OverrideChanged += enabled =>
            chatGui.Print(enabled
                ? string.IsNullOrEmpty(this.questionableIpc.CurrentQuestId)
                    ? "Daedalus rotation started — Questionable combat (objective mobs / cleanup)."
                    : $"Daedalus rotation started — Questionable kill step (quest {this.questionableIpc.CurrentQuestId})."
                : "Daedalus rotation stopped — Questionable combat done.");

        if (fightSummaryService != null)
        {
            var fightSummaryWindow = new FightSummaryWindow(
                fightSummaryService, framework, configuration,
                () => { analyticsWindow.IsOpen = true; });
            windowSystem.AddWindow(fightSummaryWindow);
        }

        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(mainWindow);
        windowSystem.AddWindow(controlWindow);
        windowSystem.AddWindow(navControlWindow);
        windowSystem.AddWindow(raidWindow);
        windowSystem.AddWindow(missingWindow);
        if (lanPartyWindow != null)
            windowSystem.AddWindow(lanPartyWindow);
        windowSystem.AddWindow(debugWindow);
        windowSystem.AddWindow(welcomeWindow);
        windowSystem.AddWindow(analyticsWindow);
        windowSystem.AddWindow(trainingWindow);
        windowSystem.AddWindow(changelogWindow);
        windowSystem.AddWindow(hintOverlay);
        windowSystem.AddWindow(overlayWindow);
        windowSystem.AddWindow(dpsMeterWindow);
        windowSystem.AddWindow(farmWindow);
        overlayWindow.IsOpen = configuration.Overlay.IsVisible;
        windowSystem.AddWindow(actionFeedWindow);
        // Visibility is gated by DrawConditions via ActionFeed.IsVisible; keep the window open
        // so the config toggle takes effect without needing to reopen the window.
        actionFeedWindow.IsOpen = true;

        windowSystem.AddWindow(drawCanvas);

        mainWindow.IsOpen = configuration.MainWindowVisible;
        mainWindow.RespectCloseHotkey = !configuration.PreventEscapeClose;
        // Debug window always starts closed - user must explicitly open it
        debugWindow.IsOpen = false;

        pluginInterface.UiBuilder.Draw += DrawUI;
        pluginInterface.UiBuilder.OpenConfigUi += OpenConfigUI;
        pluginInterface.UiBuilder.OpenMainUi += OpenMainUI;
        pluginInterface.UiBuilder.DisableCutsceneUiHide = configuration.ShowDuringCutscenes;

        this.commandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Daedalus window (short alias: /dae). Subcommands: toggle | debug | hardcast [on|off|toggle]"
        });
        this.commandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Short alias for /daedalus."
        });

        this.framework.Update += OnFrameworkUpdate;

        // Hook territory changed to load timelines for the current zone
        clientState.TerritoryChanged += OnTerritoryChanged;

        // Load timeline for current zone if already in one
        if (clientState.TerritoryType != 0)
        {
            timelineService.LoadForZone(clientState.TerritoryType);
        }

        // Send anonymous telemetry ping (fire-and-forget)
        telemetryService.SendStartupPing(PluginVersion);

        // Check for updates in the background (delayed 15s)
        updateCheckerService.StartupCheck();
    }

    // Two overloads so the method-group conversion picks the matching delegate
    // signature on either old (Action<ushort>) or new (Action<uint>) Dalamud SDKs.
    private void OnTerritoryChanged(uint zoneId) => OnTerritoryChanged((ushort)zoneId);

    private void OnTerritoryChanged(ushort zoneId)
    {
        highEndContentService.OnTerritoryChanged(zoneId);
        dutyContentService.OnTerritoryChanged(zoneId, highEndContentService.IsHighEndZone, partyList.Length);
        dutyConfigurationService.Refresh();
        rotationManager.NotifyTerritoryChanged(zoneId);
        consumableService.OnTerritoryChanged();
        timelineService.LoadForZone(zoneId);
        combatEventService.Clear();
        hpPredictionService.ClearPendingHeals();
        damageIntakeService.Clear();
        damageIntakeService.CleanupExpiredEntries();
        healingIntakeService.Clear();
        healingIntakeService.CleanupExpiredEntries();
    }

    /// <summary>
    /// Creates and populates the service container for rotation dependency injection.
    /// </summary>
    private ServiceContainer CreateServiceContainer()
    {
        var container = new ServiceContainer();

        // Dalamud services
        container.Register<IPluginLog>(log);
        container.Register<IObjectTable>(objectTable);
        container.Register<IPartyList>(partyList);
        container.Register<IJobGauges>(jobGauges);

        // Rotations consume the duty-aware snapshot; UI and persistence use the saved configuration.
        container.Register(dutyConfigurationService.RotationConfiguration);
        container.Register<Daedalus.Services.Content.IDutyContentService, Daedalus.Services.Content.DutyContentService>(dutyContentService);
        container.Register<Daedalus.Services.Content.IDutyConfigurationService, Daedalus.Services.Content.DutyConfigurationService>(dutyConfigurationService);
        container.Register<IActionTracker, ActionTracker>(actionTracker);
        container.Register(actionService);
        container.Register<ICombatEventService, CombatEventService>(combatEventService);
        container.Register<IDamageIntakeService, DamageIntakeService>(damageIntakeService);
        container.Register<IDamageTrendService, DamageTrendService>(damageTrendService);
        container.Register<ITargetingService, TargetingService>(targetingService);
        container.Register<IGapCloserSafetyService, GapCloserSafetyService>(gapCloserSafetyService);
        container.Register<ITimeToKillService, TimeToKillService>(timeToKillService);
        container.Register<IHpPredictionService, HpPredictionService>(hpPredictionService);
        container.Register<IPlayerStatsService, PlayerStatsService>(playerStatsService);
        container.Register<IDebuffDetectionService, DebuffDetectionService>(debuffDetectionService);

        // Healer services
        container.Register(healingSpellSelector);
        container.Register<ICooldownPlanner, CooldownPlanner>(cooldownPlanner);
        container.Register<IShieldTrackingService, ShieldTrackingService>(shieldTrackingService);

        // Tank services
        container.Register<IEnmityService, EnmityService>(enmityService);
        container.Register<ITankCooldownService, TankCooldownService>(tankCooldownService);
        // Melee DPS services
        container.Register<IPositionalService, PositionalService>(positionalService);
        container.Register<IVNavService, MovementArbiter>(movementArbiter);
        container.Register<IMovementArbiter, MovementArbiter>(movementArbiter);
        container.Register<IBossModSafetyService, BossModSafetyService>(bossModSafetyService);
        container.Register<IBossModForecastService, BossModForecastService>(bossModForecastService);
        container.Register<IDpsMeterService, DpsMeterService>(dpsMeterService);
        container.Register<IBluLoadoutService, BluLoadoutService>(bluLoadoutService);
        container.Register<IPositionalMovementService, PositionalMovementService>(positionalMovementService);
        container.Register(samuraiPositionalAnticipationProvider);
        container.Register(ninjaPositionalAnticipationProvider);

        // DPS burst window service
        container.Register<IBurstWindowService, BurstWindowService>(burstWindowService);

        // Tincture automation
        container.Register<Daedalus.Services.Pull.IPullIntentService, Daedalus.Services.Pull.PullIntentService>(pullIntentService);
        container.Register<Daedalus.Services.Consumables.ITinctureDispatcher, Daedalus.Services.Consumables.TinctureDispatcher>(tinctureDispatcher);

        // Player-intent override service
        container.Register<IModifierKeyService, ModifierKeyService>(modifierKeyService);

        // Smart AoE service for directional ability optimization
        container.Register<ISmartAoEService, SmartAoEService>(smartAoEService);

        // Optional services (rotations have default null parameters)
        container.Register<ITimelineService, TimelineService>(timelineService);
        if (partyCoordinationService != null)
            container.Register<IPartyCoordinationService, PartyCoordinationService>(partyCoordinationService);
        container.Register<ITrainingService, TrainingService>(trainingService);
        container.Register<IErrorMetricsService, ErrorMetricsService>(errorMetricsService);
        if (fightSummaryService != null)
            container.Register<IFightSummaryService, FightSummaryService>(fightSummaryService);

        return container;
    }

    private static readonly string[] _emptyRosterNames = [];

    /// <summary>Feeds the invite auto-accept service; roster names only materialize when it can act.
    /// UNWIRED (2026-07-09): not called from OnFrameworkUpdate and the LAN window checkbox is removed —
    /// cross-machine accept needs more live debugging (per-box opt-in UX, cross-world prompt variant).
    /// Skeleton kept: service, config flag, and tests all stay. Re-wire by calling this per frame.</summary>
    private void UpdatePartyInviteAccept()
    {
        var enabled = configuration.PartyCoordination.AutoAcceptRosterInvites && coordinationBus != null;
        var inParty = partyList.Length > 0;

        if (!enabled || inParty)
        {
            partyInviteAcceptService.Update(false, inParty, _emptyRosterNames);
            return;
        }

        var names = new System.Collections.Generic.List<string>();
        foreach (var peer in coordinationBus!.Roster)
        {
            if (peer.CharacterName.Length > 0 && peer.SenderId != coordinationBus.LocalSenderId)
                names.Add(peer.CharacterName);
        }

        partyInviteAcceptService.Update(true, false, names);
    }

    /// <summary>Own content id via ClientStructs PlayerState (this SDK's IClientState lacks
    /// LocalContentId). 0 when unavailable — the invite helper treats that as best-effort.</summary>
    private static unsafe ulong GetLocalContentId()
    {
        try
        {
            var uiState = FFXIVClientStructs.FFXIV.Client.Game.UI.UIState.Instance();
            return uiState != null ? uiState->PlayerState.ContentId : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Local toon heartbeat for the LAN roster (null when not logged in).</summary>
    private Daedalus.Services.Network.LanHeartbeatPayload? BuildLanHeartbeat()
    {
        var player = objectTable.LocalPlayer;
        if (player == null || lanCoordinator == null)
            return null;

        // Sender id is per toon: "Name@World" — set it here so login/relog always refreshes it.
        var world = player.HomeWorld.Value.Name.ToString();
        lanCoordinator.SenderId = $"{player.Name.TextValue}@{world}";

        var jobId = player.ClassJob.RowId;
        var role = JobRegistry.IsTank(jobId) ? "Tank" : JobRegistry.IsHealer(jobId) ? "Healer" : "DPS";
        var inCombat = (player.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0;
        var enemyTarget = targetManager.Target as Dalamud.Game.ClientState.Objects.Types.IBattleNpc;
        return new Daedalus.Services.Network.LanHeartbeatPayload
        {
            CharacterName = player.Name.TextValue,
            JobId = jobId,
            JobAbbrev = player.ClassJob.Value.Abbreviation.ToString(),
            HpPercent = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 0f,
            Role = role,
            Status = clientState.IsPvP ? "PvP" : "OK",
            TargetId = enemyTarget?.GameObjectId ?? 0,
            InCombat = inCombat,
            PartyGroupId = partyList.Length > 0 ? (ulong)partyList.PartyId : 0,
            ContentId = GetLocalContentId(),
            HomeWorldId = (ushort)player.HomeWorld.RowId,
            PosX = player.Position.X,
            PosY = player.Position.Y,
            PosZ = player.Position.Z,
        };
    }

    private DateTime _lanHealerDownLastCheck = DateTime.MinValue;
    private DateTime _lanHealerDownLastBroadcast = DateTime.MinValue;

    /// <summary>
    /// Any toon detects ALL party healers dead -> broadcasts HealerDown (rate-limited to one per
    /// 10s). The designated Phoenix Down carrier subscribes on the bus; the actual item-use
    /// execution is a follow-up milestone (needs inventory/UseItem machinery) — this ships the
    /// detection + signaling half so the carrier logic has something real to hook.
    /// </summary>
    private void UpdateLanHealerDownDetector()
    {
        var now = DateTime.UtcNow;
        if ((now - _lanHealerDownLastCheck).TotalSeconds < 1) return;
        _lanHealerDownLastCheck = now;

        var player = objectTable.LocalPlayer;
        if (player == null || partyList.Length == 0) return;
        var inCombat = (player.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0;
        if (!inCombat) return;

        var healers = 0;
        var deadHealers = 0;
        var deadNames = new System.Collections.Generic.List<string>();
        foreach (var member in partyList)
        {
            if (member?.ClassJob.RowId is not { } jobId || !JobRegistry.IsHealer(jobId)) continue;
            healers++;
            if (member.CurrentHP == 0)
            {
                deadHealers++;
                deadNames.Add(member.Name.TextValue);
            }
        }

        if (healers > 0 && healers == deadHealers && (now - _lanHealerDownLastBroadcast).TotalSeconds > 10)
        {
            _lanHealerDownLastBroadcast = now;
            coordinationBus!.BroadcastHealerDown(string.Join(",", deadNames));
            log.Warning($"LAN: all {healers} healer(s) down — HealerDown broadcast");
        }
    }

    /// <summary>
    /// Resolves this toon's durable tank swap role each frame. Priority: the per-toon config
    /// preference (Settings → Tanks → Shared, the healer-role analog) wins; otherwise the LAN
    /// window's off-tank designation; otherwise Undesignated (rotations fall back to live aggro).
    /// Mirrored into the coordination service because the bus lives outside the rotation DI container.
    /// </summary>
    private void UpdateLocalTankSwapRole()
    {
        if (partyCoordinationService == null)
            return;

        var preference = configuration.PartyCoordination.PreferredTankRole;
        if (preference != Daedalus.Config.TankRolePreference.Auto)
        {
            partyCoordinationService.LocalTankSwapRole = preference == Daedalus.Config.TankRolePreference.OffTank
                ? Daedalus.Services.Party.TankSwapRole.DesignatedOffTank
                : Daedalus.Services.Party.TankSwapRole.DesignatedMainTank;
            return;
        }

        if (coordinationBus == null)
        {
            partyCoordinationService.LocalTankSwapRole = Daedalus.Services.Party.TankSwapRole.Undesignated;
            return;
        }

        var offTank = coordinationBus.OffTankSenderId;
        partyCoordinationService.LocalTankSwapRole = offTank.Length == 0
            ? Daedalus.Services.Party.TankSwapRole.Undesignated
            : offTank == coordinationBus.LocalSenderId
                ? Daedalus.Services.Party.TankSwapRole.DesignatedOffTank
                : Daedalus.Services.Party.TankSwapRole.DesignatedMainTank;
    }

    /// <summary>Zone-in: broadcast our job/role and open the 3s role-collection window.</summary>
    private void OnLanTerritoryChanged(uint territory)
    {
        var player = objectTable.LocalPlayer;
        if (player == null || coordinationBus == null)
            return;

        var jobId = player.ClassJob.RowId;
        coordinationBus.BroadcastRoleAssignment(new Daedalus.Services.Network.LanRolePayload
        {
            CharacterName = player.Name.TextValue,
            JobId = jobId,
            Role = JobRegistry.IsTank(jobId) ? "Tank" : JobRegistry.IsHealer(jobId) ? "Healer" : "DPS",
        });
    }

    private void SaveConfiguration()
    {
        configuration.MainWindowVisible = mainWindow.IsOpen;
        mainWindow.RespectCloseHotkey = !configuration.PreventEscapeClose;
        pluginInterface.UiBuilder.DisableCutsceneUiHide = configuration.ShowDuringCutscenes;
        configuration.Analytics.AnalyticsWindowVisible = analyticsWindow.IsOpen;
        configuration.Training.TrainingWindowVisible = trainingWindow.IsOpen;
        configuration.Overlay.IsVisible = overlayWindow.IsOpen;
        dutyConfigurationService.Refresh();
        pluginInterface.SavePluginConfig(configuration);
    }

    private void DrawUI()
    {
        // Only draw windows when logged in (not on login/character select screen)
        if (!clientState.IsLoggedIn)
            return;

        // Show welcome window on first run
        welcomeWindow.ShowIfNeeded();

        windowSystem.Draw();
    }

    private void OpenConfigUI() => configWindow.Toggle();

    private void OpenMainUI() => mainWindow.Toggle();

    private void OpenDebugUI() => debugWindow.Toggle();

    private void OpenAnalyticsUI() => analyticsWindow.Toggle();

    private void OpenTrainingUI() => trainingWindow.Toggle();

    private void OpenChangelogUI() => changelogWindow.Toggle();

    private void OpenOverlayUI() => overlayWindow.Toggle();

    private void OpenControlUI() => controlWindow.Toggle();

    private void OpenNavControlUI() => navControlWindow.Toggle();

    private void OpenRaidUI() => raidWindow.Toggle();

    private void OpenMissingUI() => missingWindow.Toggle();

    private void OnCommand(string command, string args)
    {
        var trimmed = args.Trim().ToLowerInvariant();
        var spaceIdx = trimmed.IndexOf(' ');
        var subcommand = spaceIdx >= 0 ? trimmed[..spaceIdx] : trimmed;
        var subArg = spaceIdx >= 0 ? trimmed[(spaceIdx + 1)..].Trim() : string.Empty;

        switch (subcommand)
        {
            case "toggle":
                configuration.Enabled = !configuration.Enabled;
                SaveConfiguration();
                DaedalusIpc.NotifyStateChanged(configuration.Enabled);
                var status = configuration.Enabled ? "enabled" : "disabled";
                chatGui.Print($"Daedalus {status}");
                log.Info($"Daedalus {status}");
                break;

            case "debug":
                debugWindow.Toggle();
                SaveConfiguration();
                break;

            case "hardcast":
                HandleHardcastCommand(subArg);
                break;

            default:
                mainWindow.Toggle();
                break;
        }
    }

    private void HandleHardcastCommand(string subArg)
    {
        var current = configuration.Resurrection.AllowHardcastRaise;
        bool newValue;

        switch (subArg)
        {
            case "on":
            case "enable":
            case "true":
                newValue = true;
                break;
            case "off":
            case "disable":
            case "false":
                newValue = false;
                break;
            case "":
            case "toggle":
                newValue = !current;
                break;
            default:
                chatGui.Print($"Usage: /daedalus hardcast [on|off|toggle]. Currently {(current ? "on" : "off")}.");
                return;
        }

        configuration.Resurrection.AllowHardcastRaise = newValue;
        SaveConfiguration();
        var state = newValue ? "enabled" : "disabled";
        chatGui.Print($"Daedalus hardcast raise {state}.");
        log.Info($"Hardcast raise {state}");
    }

    private const string AutoFaceTargetConfig = "AutoFaceTargetOnAction";

    /// <summary>
    /// Forces the game's "Auto-face Target when using an action" setting on while the rotation is enabled,
    /// and restores the player's original value when disabled/unloaded. Without this, facing-required
    /// weaponskills get refused by UseAction whenever the character isn't facing the target (e.g. AutoDuty
    /// running it around), which surfaces in Why Stuck as "rejected (line-of-sight / facing / moving?)".
    /// NOTE: look-away/gaze mechanics — gaze safety still relies on dropping target (PauseWhenNoTarget);
    /// a dedicated look-away action list is a follow-up.
    /// </summary>
    private void EnsureAutoFaceTarget()
    {
        try
        {
            if (!configuration.EffectiveEnabled)
            {
                // Fully disabled — restore the player's original value if we ever overrode it.
                if (_originalAutoFaceTarget.HasValue)
                {
                    gameConfig.UiControl.Set(AutoFaceTargetConfig, _originalAutoFaceTarget.Value);
                    _originalAutoFaceTarget = null;
                }
                return;
            }

            // Capture the player's original setting once, before we start managing it.
            var current = gameConfig.UiControl.GetBool(AutoFaceTargetConfig);
            _originalAutoFaceTarget ??= current;

            // ON while running, but OFF while a look-away/gaze is being cast so our actions don't turn
            // the character into the boss. The latch is kept so we re-enable once the gaze passes.
            var desired = !(configuration.EnableLookAwaySafety
                            && PlayerSafetyHelper.IsLookAwayMechanicActive(objectTable));
            if (current != desired)
                gameConfig.UiControl.Set(AutoFaceTargetConfig, desired);
        }
        catch
        {
            // GameConfig can throw during zone transitions / before login — ignore and retry next frame.
        }
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            // Keep "Auto-face Target on action" enabled while the rotation is active (RSR parity) so
            // facing-required weaponskills aren't rejected while AutoDuty moves the character.
            EnsureAutoFaceTarget();

            // Always update debug service frame counter
            debugService.Update();

            // Movement arbiter frame sample (BMR yield state) — must run before rotation execution so
            // gating decisions are at most one frame old when movement services submit paths.
            movementArbiter.BeginFrame();

            // LAN coordination pump: drains the socket-thread inbox, sends the 2s heartbeat,
            // ages the roster. Framework thread only — the receive thread never touches game state.
            coordinationBus?.Update();
            if (coordinationBus != null)
                UpdateLanHealerDownDetector();

            // Enforce the party target mode (Focus / Split / Kill Adds) after the bus pump so mode
            // state is current this frame. Role-gated; no-op unless a mode is active and eligible.
            partyTargetingCoordinator?.Tick();

            // Push the window's off-tank designation into the coordination service so tank rotations
            // can read their durable swap role (bus lives outside the rotation container).
            UpdateLocalTankSwapRole();

            // Always update shield tracking for accurate HP predictions
            shieldTrackingService.Update();

            // Update timeline service for sync and predictions
            timelineService.Update();

            // Refresh modifier-key state. Cheap (~3 lookups), runs every frame so
            // BurstHoldHelper sees the latest player intent before the rotation runs.
            modifierKeyService.Update();

            // Update combat state for analytics — must run before performanceTracker
            // and outside the dead-player gate so sessions end correctly when the
            // player dies mid-fight. Covers all 21 jobs (previously only healers).
            {
                var lp = objectTable.LocalPlayer;
                if (lp != null)
                {
                    var inCombat = (lp.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0;
                    combatEventService.UpdateCombatState(inCombat);
                }
                else
                {
                    combatEventService.UpdateCombatState(false);
                }
            }

            // Update performance analytics (tracks combat state independently)
            performanceTracker.Update();

            // Update DPS parser (encounter segmentation + queued damage events)
            dpsMeterService.Update();

            // Refresh the BLU active spell set (throttled internally; no-op off-BLU)
            bluLoadoutService.Update();

            // Update training mode
            trainingService.Update();

            // Update coaching hints (v3.49.0)
            realTimeCoachingService.Update();
            hintOverlay.HandleInput();

            if (!clientState.IsLoggedIn)
                return;

            var localPlayer = objectTable.LocalPlayer;
            if (localPlayer == null)
                return;

            // Tincture automation: drive PullIntentService state machine and notify
            // ConsumableService of combat-state changes so the per-fight warning
            // throttle resets correctly. Runs outside the Enabled gate so the latch
            // state stays correct when re-enabled mid-fight, and outside the dead-player
            // gate so the combat-entry edge after a wipe correctly resets the latch.
            {
                var inCombatNow = (localPlayer.StatusFlags & Dalamud.Game.ClientState.Objects.Enums.StatusFlags.InCombat) != 0;
                consumableService.OnCombatStateChanged(inCombatNow);

                var (queuedId, queuedHostile) = ReadQueuedActionInfo();
                var castTargetHostile = false;
                if (localPlayer.IsCasting && localPlayer.CastTargetObjectId != 0)
                {
                    var castTargetObj = objectTable.SearchById(localPlayer.CastTargetObjectId);
                    castTargetHostile = castTargetObj is Dalamud.Game.ClientState.Objects.Types.IBattleNpc bn
                                        && bn.SubKind == Daedalus.Compat.BattleNpcKinds.Combatant;
                }

                pullIntentService.Update(
                    isPlayerCasting: localPlayer.IsCasting,
                    isCastTargetHostile: castTargetHostile,
                    queuedActionId: queuedId,
                    isQueuedActionHostile: queuedHostile,
                    isInCombat: inCombatNow,
                    utcNow: DateTime.UtcNow);
            }

            // Poll the automation bridges (throttled internally): rotation runs while a Henchman
            // task, an AutoDuty run, or a Questionable kill step is active. Must run before the
            // enabled gate.
            foreach (var bridge in automationBridges)
                bridge.Update();
            questionableIpc.Update();

            // Farm mode driver (throttled internally): targets profile mobs, roams spots, holds
            // the override while running. Also before the enabled gate.
            farmModeService.Update();

            // Automation-driven combat only: never grind on a training dummy. If external targeting
            // somehow lands on a striking dummy, drop the target and end the IPC override (the
            // user's master switch is untouched — manual dummy testing still works).
            if (configuration.ExternalCombatOverride
                && targetManager.Target is IBattleChara autoTarget
                && RsrCompatIpc.IsTrainingDummy(autoTarget.NameId))
            {
                targetManager.Target = null;
                configuration.ExternalCombatOverride = false;
                chatGui.Print("Daedalus: training dummy targeted during automated combat — target dropped, rotation stopped.");
                log.Warning("External-combat override aborted: training dummy (NameId {0}) was targeted.", autoTarget.NameId);
            }

            if (!configuration.EffectiveEnabled)
                return;

            if (localPlayer.CurrentHp == 0)
                return;

            // Update party coordination service (heartbeat, cleanup)
            partyCoordinationService?.Update(
                localPlayer.EntityId,
                localPlayer.ClassJob.RowId,
                configuration.EffectiveEnabled);

            // Track player-to-target distance for gap closer safety heuristics.
            gapCloserSafetyService.Update(localPlayer, targetManager.Target as IBattleChara);

            // Sample enemy HP for time-to-kill estimation (lazy enumeration; service throttles to ~1 Hz).
            timeToKillService.Update(
                objectTable.OfType<IBattleChara>()
                    .Where(o => o.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc));

            // Check if we have a rotation for the current job
            var jobId = localPlayer.ClassJob.RowId;
            if (!rotationManager.UpdateActiveRotation(jobId))
                return;

            rotationManager.Execute(localPlayer);

            UpdateBmrAiConfig(jobId);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Error in OnFrameworkUpdate");
        }
    }

    /// <summary>
    /// Auto-manages BossMod Reborn's AI movement config by role (distance + live positional, movement-only)
    /// for group content. Feeds the active melee rotation's next required positional so BMR positions for
    /// the exact GCD. No-ops (fail-open) when disabled or BMR isn't loaded.
    /// </summary>
    private void UpdateBmrAiConfig(uint jobId)
    {
        PositionalType? requiredPositional = null;
        var boundaryCamping = false;
        if (rotationManager.ActiveRotation is Daedalus.Rotation.Common.IHasPositionals hp)
        {
            requiredPositional = hp.Positionals.RequiredPositional;
            boundaryCamping = hp.Positionals.BoundaryCampingActive;
        }

        bmrAiConfigService.Update(new BmrAiConfigService.Request(
            Enabled: configuration.Nav.AutoManageBmrAi,
            JobId: jobId,
            RequiredPositional: requiredPositional,
            RangedStandDistance: configuration.Nav.BmrRangedStandDistance,
            BoundaryCampingActive: boundaryCamping));
    }

    /// <summary>
    /// Reads the currently queued action's ID and hostility for PullIntentService.
    /// Returns (null, false) if no action is queued or the lookup fails.
    /// QueuedActionId confirmed present on ActionManager at offset 0x70 in current FFXIVClientStructs.
    /// </summary>
    private unsafe (uint? queuedId, bool queuedHostile) ReadQueuedActionInfo()
    {
        var am = Daedalus.Services.SafeGameAccess.GetActionManager(errorMetricsService);
        if (am == null) return (null, false);

        var queuedId = am->QueuedActionId;
        if (queuedId == 0) return (null, false);

        var sheet = dataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>();
        if (sheet is null) return (queuedId, false);

        var rowOpt = sheet.GetRowOrDefault(queuedId);
        if (!rowOpt.HasValue) return (queuedId, false);

        return (queuedId, rowOpt.Value.CanTargetHostile);
    }

    public void Dispose()
    {
        // Save calibration data before shutdown
        HealingCalculator.SaveCalibration(configuration.Calibration);
        pluginInterface.SavePluginConfig(configuration);

        // Restore the player's original Auto-face setting if we overrode it.
        if (_originalAutoFaceTarget.HasValue)
        {
            try { gameConfig.UiControl.Set(AutoFaceTargetConfig, _originalAutoFaceTarget.Value); }
            catch { /* game shutting down — ignore */ }
            _originalAutoFaceTarget = null;
        }

        framework.Update -= OnFrameworkUpdate;
        if (lanCoordinator != null)
        {
            clientState.TerritoryChanged -= OnLanTerritoryChanged;
            coordinationBus?.Dispose();
            lanCoordinator.Dispose();
        }
        clientState.TerritoryChanged -= OnTerritoryChanged;
        combatEventService.OnAbilityUsed -= this.onAbilityUsedHandler;
        performanceTracker.OnSessionCompleted -= this.onSessionCompletedHandler;
        commandManager.RemoveHandler(CommandName);
        commandManager.RemoveHandler(CommandAlias);

        pluginInterface.UiBuilder.Draw -= DrawUI;
        pluginInterface.UiBuilder.OpenConfigUi -= OpenConfigUI;
        pluginInterface.UiBuilder.OpenMainUi -= OpenMainUI;

        actionFeedWindow.Dispose();
        lanPartyWindow?.Dispose();
        windowSystem.RemoveAllWindows();
        DaedalusIpc.Dispose();
        lanRosterIpc.Dispose();
        rsrCompatIpc.Dispose();
        foreach (var bridge in automationBridges)
            bridge.Dispose();
        questionableIpc.Dispose();
        farmModeService.Dispose();
        garlandDropSource.Dispose();
        partyCoordinationIpc?.Dispose();
        fflogsService?.Dispose();
        telemetryService.Dispose();
        updateCheckerService.Dispose();

        // Dispose rotation manager (handles all instantiated rotations)
        rotationManager.Dispose();

        // Dispose event subscribers before the container disposes their event sources
        // (dotTrackingService and healingIntakeService subscribe to CombatEventService)
        dotTrackingService.Dispose();
        healingIntakeService.Dispose();

        // Dispose container-registered services (e.g., CombatEventService)
        serviceContainer?.Dispose();

        performanceTracker.Dispose();
        dpsMeterService.Dispose();
        drawingService.Dispose();
        localization.Dispose();
    }
}
