using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Party;
using Dalamud.Plugin.Services;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services;
using Daedalus.Services.Action;
using Daedalus.Services.Cache;
using Daedalus.Services.Debuff;
using Daedalus.Services.Prediction;
using Daedalus.Services.Resource;
using Daedalus.Services.Stats;
using Daedalus.Services.Targeting;
using Daedalus.Timeline;

namespace Daedalus.Rotation.ArtemisCore.Context;

/// <summary>Beastmaster-specific context implementation.</summary>
public sealed class ArtemisContext : IArtemisContext
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

    /// <summary>Beastmaster has no role actions at all, so no Swiftcast.</summary>
    public bool HasSwiftcast => false;

    #endregion

    #region IMeleeDpsRotationContext

    public int ComboStep { get; }
    public uint LastComboAction { get; }
    public float ComboTimeRemaining { get; }

    // Positional state is reported honestly as "no requirement". Beastmaster's published kit
    // states no flank or rear bonus on any action, so the rotation never asks the player to move
    // for one — see the plan doc's open question on whether BST has positionals at all.
    public bool IsAtRear => false;
    public bool IsAtFlank => false;
    public bool TargetHasPositionalImmunity => true;
    public bool HasTrueNorth => false;

    #endregion

    #region IArtemisContext

    public ArtemisInstinctTracker Instinct { get; }
    public bool HasFamiliar { get; }
    public ArtemisDebugState Debug { get; }

    #endregion

    public ArtemisContext(
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
        MeleeDpsPartyHelper partyHelper,
        ArtemisInstinctTracker instinct,
        ArtemisDebugState debugState,
        int comboStep,
        uint lastComboAction,
        float comboTimeRemaining,
        ITimelineService? timelineService = null,
        IPluginLog? log = null)
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
        ObjectTable = objectTable;
        PartyList = partyList;
        Log = log;

        Instinct = instinct;
        Debug = debugState;

        ComboStep = comboStep;
        LastComboAction = lastComboAction;
        ComboTimeRemaining = comboTimeRemaining;

        // The familiar is an owned pet in the object table, same shape as the Scholar fairy and
        // the Summoner's egis.
        HasFamiliar = objectTable
            .OfType<IBattleNpc>()
            .Any(npc => npc.OwnerId == player.EntityId && npc.BattleNpcKind == BattleNpcSubKind.Pet);

        var totalHp = 0f;
        var lowestHp = 1f;
        var injured = 0;
        var members = 0;
        foreach (var member in partyHelper.GetAllPartyMembers(player))
        {
            var hp = partyHelper.GetHpPercent(member);
            totalHp += hp;
            members++;
            if (hp < lowestHp) lowestHp = hp;
            if (hp < 0.95f) injured++;
        }
        PartyHealthMetrics = (members > 0 ? totalHp / members : 1f, lowestHp, injured);

        Debug.ComboStep = ComboStep;
        Debug.InstinctChain = Instinct.Describe();
    }
}
