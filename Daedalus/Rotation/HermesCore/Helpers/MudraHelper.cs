using System;
using System.Diagnostics;
using Daedalus.Data;

namespace Daedalus.Rotation.HermesCore.Helpers;

/// <summary>
/// Helper class for Ninja mudra sequence tracking and execution.
/// Handles the state machine for mudra inputs and Ninjutsu execution.
/// </summary>
public sealed class MudraHelper
{
    // In-game mudra sequences time out after ~6 seconds. Use 7s as safety margin.
    internal const long SequenceTimeoutMs = 7000;
    private readonly Stopwatch _sequenceTimer = new();

    /// <summary>
    /// Current state of the mudra sequence.
    /// </summary>
    public MudraState State { get; private set; } = MudraState.Idle;

    /// <summary>
    /// The target Ninjutsu we're trying to execute.
    /// </summary>
    public NINActions.NinjutsuType TargetNinjutsu { get; private set; } = NINActions.NinjutsuType.None;

    /// <summary>
    /// First mudra in the current sequence.
    /// </summary>
    public NINActions.MudraType Mudra1 { get; private set; } = NINActions.MudraType.None;

    /// <summary>
    /// Second mudra in the current sequence.
    /// </summary>
    public NINActions.MudraType Mudra2 { get; private set; } = NINActions.MudraType.None;

    /// <summary>
    /// Third mudra in the current sequence.
    /// </summary>
    public NINActions.MudraType Mudra3 { get; private set; } = NINActions.MudraType.None;

    /// <summary>
    /// How many mudras have been input so far.
    /// </summary>
    public int MudraCount { get; private set; }

    /// <summary>
    /// Shadow Walker can lag after Suiton finishes — latch burst prep until status appears or KB fires.
    /// </summary>
    public const float SuitonBurstLatchSeconds = 20f;

    private readonly Stopwatch _suitonBurstLatch = new();

    /// <summary>True briefly after Suiton executes, before Shadow Walker appears on status list.</summary>
    public bool HasSuitonBurstLatch =>
        _suitonBurstLatch.IsRunning && _suitonBurstLatch.Elapsed.TotalSeconds < SuitonBurstLatchSeconds;

    public void MarkSuitonExecuted() => _suitonBurstLatch.Restart();

    public void ClearSuitonBurstLatch() => _suitonBurstLatch.Reset();

    /// <summary>
    /// Doton ground DoT status can lag one GCD after cast — latch prevents an immediate second Doton (Rabbit).
    /// </summary>
    public const float DotonActiveLatchSeconds = 24f;

    private readonly Stopwatch _dotonActiveLatch = new();

    public bool HasDotonActiveLatch =>
        _dotonActiveLatch.IsRunning && _dotonActiveLatch.Elapsed.TotalSeconds < DotonActiveLatchSeconds;

    public void MarkDotonExecuted() => _dotonActiveLatch.Restart();

    public void ClearDotonActiveLatch() => _dotonActiveLatch.Reset();

    /// <summary>Cap burst-prep GCD hold so party-burst KB holds cannot stall the rotation.</summary>
    public const float MaxBurstPrepGcdHoldSeconds = 4.5f;

    private readonly Stopwatch _burstPrepHoldTimer = new();

    public void BeginBurstPrepHold()
    {
        if (!_burstPrepHoldTimer.IsRunning)
            _burstPrepHoldTimer.Restart();
    }

    public void ClearBurstPrepHold() => _burstPrepHoldTimer.Reset();

    public bool ShouldReleaseBurstPrepGcdHold =>
        _burstPrepHoldTimer.IsRunning
        && _burstPrepHoldTimer.Elapsed.TotalSeconds >= MaxBurstPrepGcdHoldSeconds;

    /// <summary>
    /// Whether we're currently in the middle of a mudra sequence.
    /// Automatically resets if the sequence has been active longer than the game timeout.
    /// </summary>
    public bool IsSequenceActive
    {
        get
        {
            if (State == MudraState.Idle)
                return false;

            // RSR slot-step path never calls AdvanceSequence — timeout must not require MudraCount > 0.
            if (_sequenceTimer.ElapsedMilliseconds > SequenceTimeoutMs)
            {
                Reset();
                return false;
            }

            return true;
        }
    }

    /// <summary>Seconds since <see cref="StartSequence"/> (for stuck diagnostics / abort).</summary>
    public double SequenceElapsedSeconds =>
        _sequenceTimer.IsRunning ? _sequenceTimer.Elapsed.TotalSeconds : 0;

    private DateTime _lastPressUtc = DateTime.MinValue;

    /// <summary>
    /// Seconds since the last raw mudra press. Used to pace hand-sign submits: pressing the next
    /// mudra faster than the 0.5s mudra recast races the game's combo-state write and lands as a
    /// duplicate/invalid sign — the Rabbit Medium at Lv60 pull opens showed a 0.30s Ten→Chi gap
    /// where every clean sequence spaces 0.7-0.8s.
    /// </summary>
    public double SecondsSinceLastPress =>
        _lastPressUtc == DateTime.MinValue ? double.MaxValue : (DateTime.UtcNow - _lastPressUtc).TotalSeconds;

    /// <summary>RSR slot-step: record a successful mudra press without legacy state-machine AdvanceSequence.</summary>
    public void NotifyMudraPressed()
    {
        MudraCount++;
        _lastPressUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Whether we're ready to execute the Ninjutsu (all mudras input).
    /// </summary>
    public bool IsReadyToExecute => State == MudraState.ReadyToExecute;

    /// <summary>
    /// Starts a new mudra sequence for the specified Ninjutsu.
    /// </summary>
    /// <param name="ninjutsu">The Ninjutsu to execute.</param>
    public void StartSequence(NINActions.NinjutsuType ninjutsu)
    {
        Reset();
        TargetNinjutsu = ninjutsu;
        State = MudraState.FirstMudra;
        _sequenceTimer.Restart();

        // Pre-calculate the mudra sequence
        var sequence = NINActions.GetMudraSequence(ninjutsu);
        Mudra1 = sequence.Item1;
        Mudra2 = sequence.Item2;
        Mudra3 = sequence.Item3;
    }

    /// <summary>
    /// Gets the next mudra to input in the sequence.
    /// </summary>
    /// <returns>The next mudra action, or null if sequence is complete or invalid.</returns>
    public NINActions.MudraType GetNextMudra()
    {
        return State switch
        {
            MudraState.FirstMudra => Mudra1,
            MudraState.SecondMudra => Mudra2,
            MudraState.ThirdMudra => Mudra3,
            _ => NINActions.MudraType.None
        };
    }

    /// <summary>
    /// Advances the mudra sequence after successfully inputting a mudra.
    /// </summary>
    public void AdvanceSequence()
    {
        MudraCount++;

        State = State switch
        {
            MudraState.FirstMudra => Mudra2 != NINActions.MudraType.None
                ? MudraState.SecondMudra
                : MudraState.ReadyToExecute,
            MudraState.SecondMudra => Mudra3 != NINActions.MudraType.None
                ? MudraState.ThirdMudra
                : MudraState.ReadyToExecute,
            MudraState.ThirdMudra => MudraState.ReadyToExecute,
            _ => State
        };
    }

    /// <summary>
    /// Marks the Ninjutsu as executed and resets the sequence.
    /// </summary>
    public void CompleteSequence()
    {
        Reset();
    }

    /// <summary>
    /// Resets the mudra helper to idle state.
    /// Call this if the sequence is interrupted or needs to be cancelled.
    /// </summary>
    public void Reset()
    {
        State = MudraState.Idle;
        TargetNinjutsu = NINActions.NinjutsuType.None;
        Mudra1 = NINActions.MudraType.None;
        Mudra2 = NINActions.MudraType.None;
        Mudra3 = NINActions.MudraType.None;
        MudraCount = 0;
        _sequenceTimer.Reset();
    }

    /// <summary>
    /// Gets the number of mudras required for the target Ninjutsu.
    /// </summary>
    public int GetRequiredMudraCount()
    {
        if (Mudra3 != NINActions.MudraType.None) return 3;
        if (Mudra2 != NINActions.MudraType.None) return 2;
        if (Mudra1 != NINActions.MudraType.None) return 1;
        return 0;
    }

    /// <summary>
    /// Determines the best Ninjutsu to use based on current situation.
    /// </summary>
    /// <param name="level">Player level.</param>
    /// <param name="hasKassatsu">Whether Kassatsu is active.</param>
    /// <param name="needsSuiton">Whether we need Suiton for Kunai's Bane.</param>
    /// <param name="enemyCount">Number of nearby enemies.</param>
    /// <param name="useDoton">Whether Doton is enabled for AoE (config toggle).</param>
    /// <param name="dotonMinTargets">Minimum enemies for Doton (config value).</param>
    /// <param name="hasDotonActive">Whether Doton ground DoT is already ticking.</param>
    /// <returns>The recommended Ninjutsu to use.</returns>
    public static NINActions.NinjutsuType GetRecommendedNinjutsu(
        byte level,
        bool hasKassatsu,
        bool needsSuiton,
        int enemyCount,
        bool useDoton = true,
        int dotonMinTargets = 3,
        bool hasDotonActive = false)
    {
        // Kassatsu-enhanced Ninjutsu
        if (hasKassatsu)
        {
            // AoE situation
            if (enemyCount >= 3 && level >= NINActions.GokaMekkyaku.MinLevel)
                return NINActions.NinjutsuType.GokaMekkyaku;

            // Single target - Hyosho Ranryu is huge damage
            if (level >= NINActions.HyoshoRanryu.MinLevel)
                return NINActions.NinjutsuType.HyoshoRanryu;

            // Fallback to Raiton for lower levels
            if (level >= NINActions.Raiton.MinLevel)
                return NINActions.NinjutsuType.Raiton;

            return NINActions.NinjutsuType.FumaShuriken;
        }

        // Need Suiton for Kunai's Bane window
        if (needsSuiton && level >= NINActions.Suiton.MinLevel)
            return NINActions.NinjutsuType.Suiton;

        // AoE situations
        if (enemyCount >= 3)
        {
            // Doton for stationary AoE (configurable — enemies may move out of Doton)
            if (useDoton && !hasDotonActive && enemyCount >= dotonMinTargets
                && level >= NINActions.Doton.MinLevel)
                return NINActions.NinjutsuType.Doton;

            // Doton ticking — ABB skips Katon filler; combo fills Ten CD windows.
            if (hasDotonActive)
                return NINActions.NinjutsuType.None;

            // Katon for burst AoE when Doton is unavailable
            if (level >= NINActions.Katon.MinLevel)
                return NINActions.NinjutsuType.Katon;
        }

        // Single target - Raiton is the go-to
        if (level >= NINActions.Raiton.MinLevel)
            return NINActions.NinjutsuType.Raiton;

        // Low level fallback
        return NINActions.NinjutsuType.FumaShuriken;
    }
}

/// <summary>
/// States for the mudra input state machine.
/// </summary>
public enum MudraState
{
    /// <summary>Not currently in a mudra sequence.</summary>
    Idle,

    /// <summary>Waiting to input the first mudra.</summary>
    FirstMudra,

    /// <summary>First mudra input, waiting for second.</summary>
    SecondMudra,

    /// <summary>Second mudra input, waiting for third.</summary>
    ThirdMudra,

    /// <summary>All mudras input, ready to execute Ninjutsu.</summary>
    ReadyToExecute
}
