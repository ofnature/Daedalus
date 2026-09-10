#if DEBUG
using System;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Watches for the Beastmaster <c>Gauge</c> action firing and records the scan text it produces.
///
/// <para>
/// <b>DEBUG BUILD ONLY.</b> The entire class is inside <c>#if DEBUG</c>, so it does not exist in
/// Release — no chat hook, no writes, no cost. The <see cref="BeastCaptureLedger"/> it writes to is
/// deliberately NOT gated, because the shipped auto-capture rule reads that same table.
/// </para>
///
/// <para>
/// <b>How a scan is detected.</b> There is no "action used" event to subscribe to, and the player
/// will be casting Gauge by hand while collecting samples, so hooking the rotation would miss most
/// of them. Instead this polls Gauge's cooldown: the frame its remaining cooldown goes from zero to
/// non-zero, Gauge just fired. That catches manual and automated casts identically.
/// </para>
///
/// <para>
/// <b>Why it captures a window rather than matching a phrase.</b> Nobody has confirmed the scan
/// message's wording or which chat channel it arrives on. Matching on text would mean guessing
/// twice — once to find the message and again to parse it — and a wrong guess loses the sample
/// silently. So after a Gauge cast this arms a short window and records the next non-empty message,
/// whatever it says, storing it verbatim. Getting the parse wrong is recoverable; not capturing the
/// text at all is not.
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
    private System.Numerics.Vector3 _pendingPosition;

    /// <summary>
    /// Answers "is this beast already in the Master's Bestiary?" — null when it cannot be told.
    /// <para>
    /// Left unset by default and it matters that it is: <c>XBMManager.IsPetUnlocked(petId)</c>
    /// would answer it, but mapping a beast NAME to an XBMPet RowId needs
    /// <c>Lumina.Excel.Sheets.Experimental.XBMPet</c>, which Lumina marks evaluation-only and
    /// subject to change. Rather than build collection on a schema that may vanish, the ledger
    /// records null ("unknown") and this seam waits for the schema to stabilise.
    /// </para>
    /// </summary>
    public Func<string, bool?>? CapturedResolver { get; set; }

    /// <summary>Last raw text captured, for the debug tab to show even if it did not parse.</summary>
    public string LastRawText { get; private set; } = "";

    /// <summary>Scans seen this session, parsed or not.</summary>
    public int ScansThisSession { get; private set; }

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

        _chatGui.ChatMessage += OnChatMessage;
        _subscribed = true;
    }

    /// <summary>Poll for a Gauge cast. Call once per frame.</summary>
    public void Tick()
    {
        if (!_enabled())
            return;

        try
        {
            if (_objectTable.LocalPlayer is not { } player)
                return;
            if (player.ClassJob.RowId != JobRegistry.Beastmaster)
                return;

            var cooldown = _actionService.GetCooldownRemaining(BSTActions.Gauge.ActionId);

            // Zero → non-zero means the recast just started, i.e. Gauge fired this frame.
            if (_lastCooldown <= 0.01f && cooldown > 0.01f)
            {
                _windowOpenedUtc = DateTime.UtcNow;
                _pendingTargetName = ResolveTargetName(player);
                _pendingPosition = player.Position;
            }

            _lastCooldown = cooldown;
            _ledger.Tick();
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[GaugeScan] tick failed");
        }
    }

    private string ResolveTargetName(IBattleChara player)
    {
        // The scan targets whatever the player has hard-targeted. Falls back to empty rather than
        // to a nearby enemy — attributing a scan to the wrong beast would poison the table.
        var target = player.TargetObject;
        return target?.Name.TextValue ?? "";
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
            if (string.IsNullOrWhiteSpace(text))
                return;

            Handle(text);
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "[GaugeScan] chat handling failed");
        }
    }

    /// <summary>Internal seam so the attribution and record-building can be tested without a game.</summary>
    internal void Handle(string text)
    {
        var parsed = GaugeScanParser.Parse(text, _pendingTargetName);

        // The name comes from the actual target, not from prose. Without one there is nothing to
        // key the row on, so the sample is logged and dropped rather than filed under a guess.
        var name = parsed.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            LastRawText = text;
            _log?.Information("[GaugeScan] no target name for scan text: {Text}", text);
            return;
        }

        LastRawText = text;
        ScansThisSession++;
        _windowOpenedUtc = DateTime.MinValue;   // one message per cast

        _ledger.Record(
            name: name!,
            level: parsed.Level,
            difficulty: parsed.Difficulty,
            capturable: parsed.Capturable,
            territoryId: (ushort)_clientState.TerritoryType,
            territoryName: ResolveTerritoryName(),
            playerPosition: _pendingPosition,
            alreadyCaptured: CapturedResolver?.Invoke(name!),
            rawText: text);

        _log?.Information(
            "[GaugeScan] {Name} lv{Level} {Tier} — {Text}", name, parsed.Level, parsed.Difficulty, text);
    }

    private string ResolveTerritoryName()
    {
        try
        {
            var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()
                ?.GetRowOrDefault(_clientState.TerritoryType);
            if (row is null)
                return "";

            var place = row.Value.PlaceName.ValueNullable;
            return place?.Name.ExtractText() ?? "";
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
