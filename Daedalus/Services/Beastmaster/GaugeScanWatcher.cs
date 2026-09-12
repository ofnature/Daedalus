#if DEBUG
using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Watches for the Beastmaster <c>Gauge</c> action firing and records the reply it produces.
///
/// <para>
/// <b>DEBUG BUILD ONLY.</b> The entire class is inside <c>#if DEBUG</c>, so it does not exist in
/// Release — no chat hook, no writes, no cost. The <see cref="BeastCaptureLedger"/> it writes to is
/// deliberately NOT gated, because the shipped auto-capture rule reads that same table.
/// </para>
///
/// <para>
/// <b>How a scan is detected.</b> There is no "action used" event, and Gauge is mostly cast by hand
/// while collecting samples, so hooking the rotation would miss most of them. Instead this polls
/// Gauge's cooldown: the frame it goes from zero to non-zero, Gauge just fired. That catches manual
/// and automated casts identically.
/// </para>
///
/// <para>
/// <b>Which message is the reply.</b> After a cast, a short window opens and the first line that
/// <see cref="GaugeScanParser.IsScanReply"/> accepts is taken — a whole-word "befriend…" or "pact",
/// the two vocabularies real replies use. The lines observed arriving in the same window — "You
/// have left the sanctuary.", the FATE level-sync prompts, experience gains — carry neither. Taking
/// the first line of any kind, as the first version did, worked on the first four scans only
/// because of message order.
/// </para>
/// </summary>
public sealed class GaugeScanWatcher : IDisposable
{
    /// <summary>How long after a Gauge cast a chat message is attributed to that scan.</summary>
    private const double WindowSeconds = 3.0;

    private readonly IChatGui _chatGui;
    private readonly IObjectTable _objectTable;
    private readonly IClientState _clientState;
    private readonly IDataManager _dataManager;
    private readonly Daedalus.Services.Action.IActionService _actionService;
    private readonly BeastCaptureLedger _ledger;
    private readonly Func<bool> _enabled;
    private readonly IPluginLog? _log;

    private bool _subscribed;
    private double _lastCooldown;
    private DateTime _windowOpenedUtc = DateTime.MinValue;
    private string _pendingTargetName = "";
    private int _pendingTargetLevel;
    private System.Numerics.Vector3 _pendingPosition;

    /// <summary>Last raw scan reply captured, for the debug tab.</summary>
    public string LastRawText { get; private set; } = "";

    /// <summary>Scans seen this session.</summary>
    public int ScansThisSession { get; private set; }

    /// <summary>Rows the phrase table corrected on load. Shown in the debug tab.</summary>
    public int RowsReparsedOnLoad { get; }

    public GaugeScanWatcher(
        IChatGui chatGui,
        IObjectTable objectTable,
        IClientState clientState,
        IDataManager dataManager,
        Daedalus.Services.Action.IActionService actionService,
        BeastCaptureLedger ledger,
        Func<bool> enabled,
        IPluginLog? log = null)
    {
        _chatGui = chatGui;
        _objectTable = objectTable;
        _clientState = clientState;
        _dataManager = dataManager;
        _actionService = actionService;
        _ledger = ledger;
        _enabled = enabled;
        _log = log;

        // Bring every stored row up to date with the current phrase table. This is the payoff of
        // keeping raw text: rows logged while the table was wrong get corrected without a rescan.
        RowsReparsedOnLoad = _ledger.Reparse(GaugeScanParser.Derive);
        if (RowsReparsedOnLoad > 0)
            _log?.Information("[GaugeScan] re-derived {Count} ledger row(s) from stored scan text", RowsReparsedOnLoad);

        _chatGui.ChatMessage += OnChatMessage;
        _subscribed = true;
    }

    /// <summary>Poll for a Gauge cast. Call once per frame.</summary>
    public void Tick()
    {
        try
        {
            _ledger.Tick();

            if (!_enabled())
                return;
            if (_objectTable.LocalPlayer is not { } player)
                return;
            if (player.ClassJob.RowId != JobRegistry.Beastmaster)
                return;

            var cooldown = _actionService.GetCooldownRemaining(BSTActions.Gauge.ActionId);

            // Zero → non-zero means the recast just started, i.e. Gauge fired this frame.
            if (_lastCooldown <= 0.01f && cooldown > 0.01f)
            {
                // Snapshot the target NOW: by the time the reply arrives the player may have
                // retargeted, and attributing a scan to the wrong beast would poison the table.
                var target = player.TargetObject;
                _windowOpenedUtc = DateTime.UtcNow;
                _pendingTargetName = target?.Name.TextValue ?? "";
                _pendingTargetLevel = target is ICharacter c ? c.Level : 0;
                _pendingPosition = player.Position;
            }

            _lastCooldown = cooldown;
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[GaugeScan] tick failed");
        }
    }

    private void OnChatMessage(Dalamud.Game.Chat.IHandleableChatMessage message)
    {
        if (!_enabled())
            return;

        try
        {
            if (_windowOpenedUtc == DateTime.MinValue)
                return;
            if ((DateTime.UtcNow - _windowOpenedUtc).TotalSeconds > WindowSeconds)
                return;

            var text = message.Message?.TextValue ?? "";
            if (!GaugeScanParser.IsScanReply(text))
            {
                // Chat noise inside the window. Logged rather than dropped silently, so a Gauge reply
                // in a third vocabulary nobody has seen yet would still be visible in /xllog — the
                // "pact" reply was exactly that, until it was.
                if (!string.IsNullOrWhiteSpace(text))
                    _log?.Debug("[GaugeScan] ignored non-reply in scan window: {Text}", text);
                return;
            }

            Handle(text);
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[GaugeScan] chat handling failed");
        }
    }

    /// <summary>Record one scan reply against the target snapshotted when Gauge fired.</summary>
    internal void Handle(string text)
    {
        var parsed = GaugeScanParser.Parse(text, _pendingTargetName);
        LastRawText = text;

        // The reply only ever says "this beast". With no target snapshot there is nothing to key the
        // row on, so the sample is logged and dropped rather than filed under a guess.
        if (string.IsNullOrWhiteSpace(parsed.Name))
        {
            _log?.Information("[GaugeScan] no target name for scan reply: {Text}", text);
            return;
        }

        ScansThisSession++;
        _windowOpenedUtc = DateTime.MinValue;   // one reply per cast

        _ledger.Record(
            name: parsed.Name!,
            level: _pendingTargetLevel,
            difficulty: parsed.Difficulty,
            capturable: parsed.Capturable,
            territoryId: (ushort)_clientState.TerritoryType,
            territoryName: ResolveTerritoryName(),
            playerPosition: _pendingPosition,
            alreadyCaptured: parsed.AlreadyCaptured,
            rawText: text);

        _log?.Information(
            "[GaugeScan] {Name} lv{Level} {Tier} capturable={Cap} owned={Owned} — {Text}",
            parsed.Name, _pendingTargetLevel, parsed.Difficulty, parsed.Capturable, parsed.AlreadyCaptured, text);
    }

    private string ResolveTerritoryName()
    {
        try
        {
            var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                ?.GetRowOrDefault(_clientState.TerritoryType);
            return row?.PlaceName.ValueNullable?.Name.ExtractText() ?? "";
        }
        catch
        {
            return "";
        }
    }

    public void Dispose()
    {
        if (!_subscribed)
            return;

        try { _chatGui.ChatMessage -= OnChatMessage; }
        catch { /* shutting down */ }

        _subscribed = false;
        _ledger.Save();
    }
}
#endif
