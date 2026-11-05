using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Config;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;

namespace Daedalus.Services.Eureka;

/// <summary>
/// Eureka: reads elemental levels and elements (<c>BattleChara.GetForayInfo</c>, as BossMod does) and the Magia
/// Board (<c>PublicContentEureka.GetState</c>), and keeps the board turned with the game's own /magiaauto, so the
/// game does the element matching. Inert outside the four Eureka zones.
/// </summary>
public sealed unsafe class EurekaService : IDisposable
{
    private static readonly TimeSpan SendEvery = TimeSpan.FromSeconds(3);
    private const int MaxRefusals = 5;

    private readonly Configuration _configuration;
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IObjectTable _objectTable;
    private readonly IPluginLog _log;
    private readonly IChatGui _chatGui;

    private string? _sentAuto;
    private DateTime _nextSendUtc;
    private DateTime _arrivedUtc;
    private volatile bool _refused;
    private int _refusals;

    /// <summary>The live instance, for the settings page's readout.</summary>
    public static EurekaService? Current { get; private set; }

    public EurekaService(
        Configuration configuration, IClientState clientState, ICondition condition, IObjectTable objectTable, IPluginLog log, IChatGui chatGui)
    {
        Current = this;
        _configuration = configuration;
        _clientState = clientState;
        _condition = condition;
        _objectTable = objectTable;
        _log = log;
        _chatGui = chatGui;
        _chatGui.ChatMessage += OnChatMessage;
    }

    public void Dispose() => _chatGui.ChatMessage -= OnChatMessage;

    /// <summary>The game refusing a /magiaauto: send it again later rather than think the board is set.</summary>
    private void OnChatMessage(Dalamud.Game.Chat.IHandleableChatMessage message)
    {
        try
        {
            if (_sentAuto != null && EurekaRules.IsMagiaAutoRefused(message.Message.TextValue))
                _refused = true;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "EurekaService: chat check failed");
        }
    }

    public bool InEureka => EurekaRules.IsEurekaTerritory(_clientState.TerritoryType);

    public IPlayerCharacter? Player => _objectTable.LocalPlayer;

    /// <summary>The /magiaauto setting last sent this visit, or null.</summary>
    public string? MagiaAuto => _sentAuto;

    /// <summary>An object's Eureka elemental level and element, or null when it has none (0/0 outside Eureka).</summary>
    public static (byte Level, byte Element)? Foray(IGameObject? obj)
    {
        if (obj is not IBattleChara || obj.Address == IntPtr.Zero)
            return null;
        var info = ((BattleChara*)obj.Address)->GetForayInfo();
        if (info == null || (info->Level == 0 && info->Element == 0))
            return null;
        return (info->Level, info->Element);
    }

    /// <summary>
    /// Whether Farm mode should leave this enemy alone: more than the allowed levels above us. Never outside Eureka
    /// (no levels to read there).
    /// </summary>
    public static bool TooHighToPull(IGameObject enemy, IPlayerCharacter player, EurekaConfig cfg)
        => Foray(enemy) is { } them && Foray(player) is { } me
           && EurekaRules.TooHighToPull(me.Level, them.Level, cfg.MaxLevelsAbove);

    /// <summary>The Magia Board and elemental EXP, or null outside Eureka.</summary>
    public (byte MagiaAether, byte Magicite, uint Exp, uint ExpNeeded, byte[] Placed)? Board()
    {
        if (!InEureka)
            return null;
        var state = PublicContentEureka.GetState();
        if (state == null)
            return null;
        return (state->MagiaAether, state->Magicite, state->CurrentExperience, state->NeededExperience,
            [state->Fire, state->Ice, state->Wind, state->Earth, state->Lightning, state->Water]);
    }

    /// <summary>Framework thread, every frame.</summary>
    public void Update(IPlayerCharacter? player, bool isTank)
    {
        try
        {
            if (player == null || !InEureka || _condition[ConditionFlag.BetweenAreas] || _condition[ConditionFlag.OccupiedInCutSceneEvent])
            {
                if (!InEureka)
                {
                    _sentAuto = null;
                    _refusals = 0;
                }

                _arrivedUtc = default;

                return;
            }

            var now = DateTime.UtcNow;
            if (_arrivedUtc == default)
                _arrivedUtc = now;
            if (now - _arrivedUtc < EurekaRules.SettleAfterArrival)
                return;
            if (_refused)
            {
                // A few tries per visit, so a board that stays locked does not fill the chat with errors.
                _refused = false;
                if (++_refusals <= MaxRefusals)
                {
                    _log.Info($"Eureka: the game refused /magiaauto {_sentAuto}; trying again shortly.");
                    _sentAuto = null;
                    _nextSendUtc = now + EurekaRules.RetryAfterRefusal;
                }
            }

            var wanted = EurekaRules.WantedAuto(_configuration.Eureka.MagiaMode, isTank);
            if (wanted == null || wanted == _sentAuto || now < _nextSendUtc)
                return;

            _nextSendUtc = now + SendEvery;
            if (!SendCommand($"/magiaauto {wanted}"))
                return;
            _sentAuto = wanted;
            _log.Info($"Eureka: Magia Board set to auto {wanted}");
            Daedalus.Rotation.Base.RotationServices.DebugLog?.Log(
                Daedalus.Services.Debug.DebugLogCategory.Action, Daedalus.Services.Debug.DebugLogSeverity.Info,
                $"[Eureka] Magia Board: /magiaauto {wanted}");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "EurekaService: update failed");
        }
    }

    /// <summary>A game text command (not a plugin one), as if typed in chat.</summary>
    private static bool SendCommand(string text)
    {
        var ui = UIModule.Instance();
        if (ui == null)
            return false;
        var str = Utf8String.FromString(text);
        ui->ProcessChatBoxEntry(str, IntPtr.Zero, false);
        str->Dtor(true);
        return true;
    }
}
