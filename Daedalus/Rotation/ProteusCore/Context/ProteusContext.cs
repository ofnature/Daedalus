using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Config.DPS;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.ProteusCore.Helpers;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Cache;
using Daedalus.Services.Debuff;
using Daedalus.Services.Party;
using Daedalus.Services.Prediction;
using Daedalus.Services.Resource;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Services.Training;
using Daedalus.Timeline;

namespace Daedalus.Rotation.ProteusCore.Context;

/// <summary>
/// Blue Mage context implementation. No gauge reads — MP, statuses, and the configured role only.
/// </summary>
public sealed class ProteusContext : IProteusContext
{
    #region IRotationContext

    public IPlayerCharacter Player { get; }
    public bool InCombat { get; }
    public bool IsMoving { get; }
    public bool CanExecuteGcd { get; }
    public bool CanExecuteOgcd { get; }

    public IActionService ActionService { get; }
    public IActionTracker ActionTracker { get; }
    public ICombatEventService CombatEventService { get; }
    public IDamageIntakeService DamageIntakeService { get; }
    public IDamageTrendService DamageTrendService { get; }
    public IFrameScopedCache FrameCache { get; }
    public Configuration Configuration { get; }
    public IDebuffDetectionService DebuffDetectionService { get; }
    public IHpPredictionService HpPredictionService { get; }
    public IMpForecastService MpForecastService { get; }
    public IPlayerStatsService PlayerStatsService { get; }
    public ITargetingService TargetingService { get; }
    public ITimelineService? TimelineService { get; }

    public IObjectTable ObjectTable { get; }
    public IPartyList PartyList { get; }
    public IPluginLog? Log { get; }

    public (float avgHpPercent, float lowestHpPercent, int injuredCount) PartyHealthMetrics { get; }

    public IPartyCoordinationService? PartyCoordinationService { get; }

    #endregion

    #region ICasterDpsRotationContext

    public int CurrentMp { get; }
    public int MaxMp { get; }
    public float MpPercent { get; }

    public bool IsCasting { get; }
    public float CastRemaining { get; }
    public bool CanSlidecast { get; }

    public bool HasSwiftcast { get; }
    public bool HasTriplecast => false; // BLU has no Triplecast
    public int TriplecastStacks => 0;
    public bool HasInstantCast { get; }

    #endregion

    #region IProteusContext

    public BluRole Role { get; }
    public bool HasMightyGuard { get; }
    public bool HasDiamondback { get; }
    public bool HasCorrectMimicry { get; }
    public bool HasAnyMimicry { get; }
    public IBluLoadoutService? LoadoutService { get; }

    public bool IsSpellUsable(uint actionId)
        => ActionService.IsActionLearned(actionId) && (LoadoutService?.IsSlotted(actionId) ?? true);

    public ProteusStatusHelper StatusHelper { get; }
    public CasterPartyHelper PartyHelper { get; }
    public ProteusDebugState Debug { get; }
    public ITrainingService? TrainingService { get; }

    #endregion

    public ProteusContext(
        IPlayerCharacter player,
        bool inCombat,
        bool isMoving,
        bool canExecuteGcd,
        bool canExecuteOgcd,
        IActionService actionService,
        IActionTracker actionTracker,
        ICombatEventService combatEventService,
        IDamageIntakeService damageIntakeService,
        IDamageTrendService damageTrendService,
        IFrameScopedCache frameCache,
        Configuration configuration,
        IDebuffDetectionService debuffDetectionService,
        IHpPredictionService hpPredictionService,
        IMpForecastService mpForecastService,
        IPlayerStatsService playerStatsService,
        ITargetingService targetingService,
        IObjectTable objectTable,
        IPartyList partyList,
        ProteusStatusHelper statusHelper,
        CasterPartyHelper partyHelper,
        ProteusDebugState debugState,
        ITimelineService? timelineService = null,
        ITrainingService? trainingService = null,
        IPartyCoordinationService? partyCoordinationService = null,
        IPluginLog? log = null,
        IBluLoadoutService? loadoutService = null)
    {
        Player = player;
        InCombat = inCombat;
        IsMoving = isMoving;
        CanExecuteGcd = canExecuteGcd;
        CanExecuteOgcd = canExecuteOgcd;
        ActionService = actionService;
        ActionTracker = actionTracker;
        CombatEventService = combatEventService;
        DamageIntakeService = damageIntakeService;
        DamageTrendService = damageTrendService;
        FrameCache = frameCache;
        Configuration = configuration;
        DebuffDetectionService = debuffDetectionService;
        HpPredictionService = hpPredictionService;
        MpForecastService = mpForecastService;
        PlayerStatsService = playerStatsService;
        TargetingService = targetingService;
        TimelineService = timelineService;
        TrainingService = trainingService;
        ObjectTable = objectTable;
        PartyList = partyList;
        Log = log;
        PartyCoordinationService = partyCoordinationService;

        StatusHelper = statusHelper;
        PartyHelper = partyHelper;
        Debug = debugState;

        CurrentMp = (int)player.CurrentMp;
        MaxMp = (int)player.MaxMp;
        MpPercent = MaxMp > 0 ? (float)CurrentMp / MaxMp : 0f;

        IsCasting = player.IsCasting;
        CastRemaining = player.TotalCastTime - player.CurrentCastTime;
        CanSlidecast = IsCasting && CastRemaining <= 0.5f;

        HasSwiftcast = BaseStatusHelper.HasSwiftcast(player);
        HasInstantCast = HasSwiftcast;

        Role = configuration.BlueMage.Role;
        HasMightyGuard = statusHelper.HasMightyGuard(player);
        HasDiamondback = statusHelper.HasDiamondback(player);
        HasCorrectMimicry = statusHelper.HasMimicryForRole(player, Role);
        HasAnyMimicry = statusHelper.HasAnyMimicry(player);
        LoadoutService = loadoutService;

        PartyHealthMetrics = CalculatePartyHealth(player);

        Debug.Role = Role.ToString();
        Debug.HasMightyGuard = HasMightyGuard;
        Debug.ActiveMimicry = statusHelper.GetActiveMimicryName(player);
        Debug.Loadout = loadoutService is { HasSlotData: true }
            ? $"{loadoutService.SlottedCount}/{BluLoadoutService.SlotCount} slotted"
            : "no slot data";
    }

    private (float avgHpPercent, float lowestHpPercent, int injuredCount) CalculatePartyHealth(IPlayerCharacter player)
    {
        var totalHp = 0f;
        var lowestHp = 1f;
        var injuredCount = 0;
        var memberCount = 0;

        foreach (var member in PartyHelper.GetAllPartyMembers(player))
        {
            var hp = PartyHelper.GetHpPercent(member);
            totalHp += hp;
            memberCount++;

            if (hp < lowestHp)
                lowestHp = hp;

            if (hp < 0.95f)
                injuredCount++;
        }

        var avgHp = memberCount > 0 ? totalHp / memberCount : 1f;
        return (avgHp, lowestHp, injuredCount);
    }
}
