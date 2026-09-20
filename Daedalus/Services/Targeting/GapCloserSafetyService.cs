using System.Diagnostics;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Targeting;

/// <summary>
/// Tracks player-to-target distance over a short lookback window to decide whether
/// gap closers are safe to fire. See <see cref="IGapCloserSafetyService"/> for the
/// design rationale.
/// </summary>
public sealed class GapCloserSafetyService : IGapCloserSafetyService
{
    private readonly Configuration _configuration;
    private readonly ITargetManager _targetManager;

    // Circular buffer of recent (timestamp_ms, distance) samples for the player's
    // current target. Small (8 entries) because we only need the oldest sample
    // within the lookback window. Reset whenever the tracked target changes.
    private readonly (long timestampMs, float distance)[] _samples = new (long, float)[8];
    private int _sampleCount;
    private int _sampleHead; // next write index
    private ulong _trackedTargetId;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public string? LastBlockReason { get; private set; }

    /// <summary>
    /// The boss engine is walking the character this frame. A gap closer is a dash toward the target,
    /// which during a dodge is the one direction that was just ruled out; Plugin wires this to the
    /// engine router so Minerva and BossMod both answer it.
    /// </summary>
    public System.Func<bool>? ExternalSteering { get; set; }

    /// <summary>
    /// How long the boss engine says the ground underfoot stays safe, in seconds. A gap closer roots the
    /// character for the length of its dash, so a spot that is about to become dangerous is a spot it must
    /// not be spent on. Plugin wires this to the engine router alongside <see cref="ExternalSteering"/>.
    /// </summary>
    public System.Func<float>? SecondsSafeHere { get; set; }

    /// <summary>
    /// What a gap closer costs in stillness: the dash plus its animation lock, measured on Onslaught.
    /// Accept No Imitators, 2026-09-06: Onslaught fired 0.1s into a cast, the dodge wanted five yalms and
    /// could not move until it ended, and the hit landed with a vulnerability stack. Steering had not begun
    /// yet, so the "is the engine steering" gate could not see it coming -- the ground could.
    /// </summary>
    public const float GapCloserRootSeconds = 0.7f;

    public GapCloserSafetyService(Configuration configuration, ITargetManager targetManager)
    {
        _configuration = configuration;
        _targetManager = targetManager;
    }

    /// <inheritdoc />
    public void Update(IPlayerCharacter? player, IBattleChara? currentTarget)
    {
        if (player == null || currentTarget == null)
        {
            ResetSamples(0UL);
            return;
        }

        // Reset tracking when the target changes — old distance samples are meaningless.
        if (currentTarget.GameObjectId != _trackedTargetId)
        {
            ResetSamples(currentTarget.GameObjectId);
        }

        var distance = Vector2.Distance(
            new Vector2(player.Position.X, player.Position.Z),
            new Vector2(currentTarget.Position.X, currentTarget.Position.Z));

        var nowMs = _clock.ElapsedMilliseconds;

        _samples[_sampleHead] = (nowMs, distance);
        _sampleHead = (_sampleHead + 1) % _samples.Length;
        if (_sampleCount < _samples.Length)
            _sampleCount++;
    }

    /// <inheritdoc />
    public bool ShouldBlockGapCloser(IBattleChara target, IPlayerCharacter player)
    {
        // Master toggle off → never block.
        if (!_configuration.Targeting.SafeGapCloser)
        {
            LastBlockReason = null;
            return false;
        }

        // The dodge owns the character: do not dash it back toward the boss.
        if (ExternalSteering?.Invoke() == true)
        {
            LastBlockReason = "boss engine is steering a dodge";
            return true;
        }

        // Not steering yet is not the same as safe. A dash roots the character for its duration, so if the
        // ground stops being safe inside that window the dodge will want to move and will not be able to.
        if (SecondsSafeHere?.Invoke() is { } safeFor && safeFor < GapCloserRootSeconds + 0.3f)
        {
            LastBlockReason = $"ground is only safe for {safeFor:0.0}s and the dash roots for {GapCloserRootSeconds:0.0}s";
            return true;
        }

        // Belt & suspenders: if damage is paused for "no target" reasons, block too.
        if (_configuration.Targeting.PauseWhenNoTarget && _targetManager.Target == null)
        {
            LastBlockReason = "no target selected";
            return true;
        }

        // Rule 1: Only gap close onto the player's explicitly selected enemy.
        // Prevents Daedalus from dragging the player to a strategy-picked fallback
        // (e.g., LowestHp add they never meant to engage).
        var userTarget = _targetManager.Target as IBattleNpc;
        if (userTarget == null || userTarget.GameObjectId != target.GameObjectId)
        {
            LastBlockReason = "target mismatch (only gap-close to your explicit target)";
            return true;
        }

        // Rule 2: If the player has been gaining distance from the target within the
        // lookback window, they are deliberately repositioning — don't yank them back.
        // Covers spread markers, stack markers, ground AoE, gaze mechanics that the
        // player is currently avoiding.
        if (IsPlayerMovingAway(target, player))
        {
            LastBlockReason = "moving away from target";
            return true;
        }

        LastBlockReason = null;
        return false;
    }

    private bool IsPlayerMovingAway(IBattleChara target, IPlayerCharacter player)
    {
        if (_sampleCount < 2)
            return false;

        var lookbackMs = _configuration.Targeting.GapCloserMovementLookbackMs;
        var awayThreshold = _configuration.Targeting.GapCloserMovementAwayThresholdY;
        var nowMs = _clock.ElapsedMilliseconds;

        // Find the oldest sample still inside the lookback window.
        float oldestDistance = -1f;
        long oldestAgeMs = 0;
        for (int i = 0; i < _sampleCount; i++)
        {
            var idx = (_sampleHead - 1 - i + _samples.Length) % _samples.Length;
            var sample = _samples[idx];
            var ageMs = nowMs - sample.timestampMs;
            if (ageMs <= lookbackMs)
            {
                oldestDistance = sample.distance;
                oldestAgeMs = ageMs;
            }
            else
            {
                break;
            }
        }

        if (oldestDistance < 0f || oldestAgeMs < 100)
            return false;

        var currentDistance = Vector2.Distance(
            new Vector2(player.Position.X, player.Position.Z),
            new Vector2(target.Position.X, target.Position.Z));

        return currentDistance - oldestDistance >= awayThreshold;
    }

    private void ResetSamples(ulong newTargetId)
    {
        _trackedTargetId = newTargetId;
        _sampleCount = 0;
        _sampleHead = 0;
    }
}
