using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using Daedalus.Config;

namespace Daedalus.Services.Occult;

/// <summary>
/// Tracks a pot FATE treasure hunt: every Magical Elixir reading becomes a bearing, and the
/// overlap of those bearings is where the coffer is.
/// <para>
/// Headless by design — it holds the readings and nothing else draws from it yet. The map and
/// the in-world activation ring read <see cref="Bearings"/>; keeping them out of here means the
/// hunt logic is testable without ImGui or a game session.
/// </para>
/// <para>
/// It also measures what we currently guess. Only "immediately" (&lt;10y) is a confirmed band,
/// and the activation radius is unknown entirely — but the moment the coffer spawns, both become
/// observable: the distance from each earlier reading to the find, and the player's distance at
/// the instant it appeared.
/// </para>
/// </summary>
public sealed class PotTreasureHunt : IDisposable
{
    /// <summary>Keep the sample lists bounded; far more than enough to pin either number.</summary>
    public const int MaxSamples = 500;

    private readonly IChatGui? _chatGui;
    private readonly IObjectTable? _objectTable;
    private readonly IClientState? _clientState;
    private readonly PhantomConfig? _config;
    private readonly System.Action? _save;
    private readonly IPluginLog? _log;

    private readonly List<ElixirBearing> _bearings = [];
    private bool _huntActive;
    private ushort _lastZone;
    private bool _subscribed;

    public PotTreasureHunt(
        IChatGui? chatGui,
        IObjectTable? objectTable,
        IClientState? clientState,
        PhantomConfig? config,
        System.Action? save = null,
        IPluginLog? log = null)
    {
        _chatGui = chatGui;
        _objectTable = objectTable;
        _clientState = clientState;
        _config = config;
        _save = save;
        _log = log;

        if (_chatGui is null)
            return;

        try
        {
            _chatGui.ChatMessage += OnChatMessage;
            _subscribed = true;
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "PotTreasureHunt: could not subscribe to chat");
        }
    }

    /// <summary>Readings taken so far this hunt, oldest first.</summary>
    public IReadOnlyList<ElixirBearing> Bearings => _bearings;

    /// <summary>True while "Cache Me if You Can" is up.</summary>
    public bool IsHunting => _huntActive;

    /// <summary>Where the last hunt's coffer was found, for the accuracy check.</summary>
    public Vector3? LastFoundAt { get; private set; }

    /// <summary>
    /// True when the last find fell OUTSIDE its own readings. That means an assumption is wrong —
    /// the arc is narrower than we think, or a band edge is off — and it is worth surfacing
    /// rather than quietly tolerating.
    /// </summary>
    public bool LastFindContradictedReadings { get; private set; }

    /// <summary>Framework tick. Cheap: a status read and, only while hunting, a coffer scan.</summary>
    public void Update()
    {
        if (_clientState is null || _objectTable is null)
            return;

        var zone = (ushort)_clientState.TerritoryType;
        if (zone != _lastZone)
        {
            _lastZone = zone;
            Reset();
            return;
        }

        var player = _objectTable.LocalPlayer;
        var hunting = IsTreasureHuntActive(player);

        if (!hunting)
        {
            // Status gone: either the coffer was found (handled below) or the hunt lapsed.
            if (_huntActive)
                Reset();
            _huntActive = false;
            return;
        }

        _huntActive = true;

        // The coffer has no object until you are within interact range, so its appearance IS the
        // moment of discovery — and the only hard measurement a hunt ever yields.
        if (player is null)
            return;

        foreach (var obj in _objectTable)
        {
            if (obj.ObjectKind != ObjectKind.EventObj)
                continue;
            if (!ChestLedger.IsCofferName(obj.Name.TextValue))
                continue;

            OnCofferFound(obj.Position, Vector3.Distance(player.Position, obj.Position));
            return;
        }
    }

    /// <summary>
    /// Handle one chat line. Public so the hunt can be exercised without a game session — the
    /// live path just forwards every message here with the player's current position.
    /// </summary>
    public void HandleMessage(string? text, Vector3 playerPosition)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        if (PotTreasureTriangulation.TryReadElixir(text, playerPosition, out var bearing))
        {
            _bearings.Add(bearing);
            _log?.Debug($"[PotHunt] reading {_bearings.Count}: {bearing.Proximity} @ {bearing.HeadingRadians:F2} rad");
            return;
        }

        // Fallback end-of-hunt signal. The object spawn is preferred because it carries a
        // position, but the message fires even if the coffer never entered our object table.
        if (PotTreasureTriangulation.IsDiscovery(text))
            Reset();
    }

    /// <summary>
    /// The coffer appeared. Record what it teaches — band ground truth and the activation
    /// radius — then clear, so a second hunt never inherits the first one's readings.
    /// </summary>
    private void OnCofferFound(Vector3 position, float playerDistance)
    {
        LastFoundAt = position;
        LastFindContradictedReadings = _bearings.Count > 0
            && !PotTreasureTriangulation.AllReadingsAgreeWith(_bearings, position);

        if (LastFindContradictedReadings)
        {
            _log?.Information(
                "[PotHunt] find at {0} fell outside its own readings — arc or band edges need widening",
                position);
        }

#if DEBUG
        RecordCalibration(position, playerDistance);
#endif

        _bearings.Clear();
    }

#if DEBUG
    /// <summary>
    /// DEBUG-only, like the rest of the learning: shipped builds have no business growing the
    /// config. Release reads the numbers once they are baked in.
    /// </summary>
    private void RecordCalibration(Vector3 position, float playerDistance)
    {
        if (_config is null)
            return;

        var changed = false;

        foreach (var sample in PotTreasureTriangulation.Calibrate(_bearings, position))
        {
            if (_config.PotHuntCalibration.Count >= MaxSamples)
                break;

            _config.PotHuntCalibration.Add(new PotHuntCalibrationSample
            {
                Band = sample.Band.ToString(),
                ActualDistance = sample.ActualDistance,
                AngularErrorRadians = sample.AngularErrorRadians,
            });
            changed = true;
        }

        if (playerDistance > 0f && _config.ActivationRadiusSamples.Count < MaxSamples)
        {
            _config.ActivationRadiusSamples.Add(playerDistance);
            changed = true;
        }

        if (changed)
            _save?.Invoke();
    }
#endif

    /// <summary>
    /// The LARGEST distance at which a coffer has been seen to spawn — how big the activation
    /// ring should be drawn.
    /// <para>
    /// Max rather than average deliberately. Each sample is measured on the tick we first notice
    /// the object, by which point you may already have walked past the true trigger boundary, so
    /// every reading UNDERSTATES the real radius. The largest is the closest to the truth, and
    /// erring large is the safe direction: a ring drawn too small has you standing at its edge
    /// wondering why nothing spawned.
    /// </para>
    /// Null until a hunt has been completed.
    /// </summary>
    public float? MaxObservedActivationRadius
    {
        get
        {
            if (_config?.ActivationRadiusSamples is not { Count: > 0 } samples)
                return null;

            var max = 0f;
            foreach (var sample in samples)
            {
                if (sample > max)
                    max = sample;
            }

            return max > 0f ? max : null;
        }
    }

    /// <summary>
    /// The widest angle by which a reading has ever been wrong — i.e. how wide the arc actually
    /// needs to be, measured rather than assumed.
    /// <para>
    /// Each hunt contributes one sample per reading, so this converges in a couple of runs.
    /// <para>
    /// Read it against <see cref="PotTreasureTriangulation.DefaultHalfAngleRadians"/> (22.5°),
    /// which is a CEILING: eight compass words partition 360° into 45° sectors, so a reported
    /// direction cannot mean more than ±22.5° without "south" overlapping "southeast".
    /// Comfortably below means the arc can be tightened for sharper overlaps. ABOVE means
    /// something is wrong rather than narrow — a sixteen-point compass (±11.25°), a heading
    /// convention error, or an origin recorded after the player moved.
    /// </para>
    /// </para>
    /// Null until a hunt has been completed.
    /// </summary>
    public float? MaxObservedAngularErrorRadians
    {
        get
        {
            if (_config?.PotHuntCalibration is not { Count: > 0 } samples)
                return null;

            var max = 0f;
            foreach (var sample in samples)
            {
                if (sample.AngularErrorRadians > max)
                    max = sample.AngularErrorRadians;
            }

            return max > 0f ? max : null;
        }
    }

    /// <summary>The same figure in degrees, for anything user-facing.</summary>
    public float? MaxObservedAngularErrorDegrees
        => MaxObservedAngularErrorRadians is { } radians ? radians * 180f / MathF.PI : null;

    /// <summary>Drop the current hunt's readings. A new hunt must never inherit old bearings.</summary>
    public void Reset()
    {
        _bearings.Clear();
        _huntActive = false;
    }

    private static bool IsTreasureHuntActive(Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter? player)
    {
        if (player?.StatusList is not { } statuses)
            return false;

        foreach (var status in statuses)
        {
            if (status != null && status.StatusId == PotFateTracker.TreasureHuntStatusId)
                return true;
        }

        return false;
    }

    private void OnChatMessage(Dalamud.Game.Chat.IHandleableChatMessage message)
    {
        try
        {
            if (!_huntActive || _objectTable?.LocalPlayer is not { } player)
                return;

            HandleMessage(message.Message.TextValue, player.Position);
        }
        catch (Exception ex)
        {
            _log?.Warning(ex, "PotTreasureHunt: chat handling failed");
        }
    }

    public void Dispose()
    {
        if (!_subscribed || _chatGui is null)
            return;

        try { _chatGui.ChatMessage -= OnChatMessage; }
        catch { /* shutting down */ }

        _subscribed = false;
    }
}
