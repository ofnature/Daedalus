namespace Daedalus.Rotation.PrometheusCore.Helpers;

/// <summary>
/// Tracks RSR 14-step Automaton Queen battery pairs for trial/raid content.
/// </summary>
public sealed class PrometheusQueenTracker
{
    private static readonly (byte From, byte To)[] StepPairs =
    [
        (0, 60),
        (60, 90),
        (90, 100),
        (100, 50),
        (50, 60),
        (60, 100),
        (100, 50),
        (50, 70),
        (70, 100),
        (100, 50),
        (50, 80),
        (70, 100),
        (100, 50),
        (50, 60),
    ];

    private int _currentStep;
    private byte _lastTrackedSummonBattery;

    public int CurrentStep => _currentStep;

    public void Reset()
    {
        _currentStep = 0;
        _lastTrackedSummonBattery = 0;
    }

    /// <summary>
    /// Advances the step when a new Queen summon is detected via gauge LastQueenBattery.
    /// </summary>
    public void OnFrame(int lastQueenBattery)
    {
        var tracked = (byte)System.Math.Clamp(lastQueenBattery, 0, 100);
        if (_lastTrackedSummonBattery == tracked)
            return;

        _lastTrackedSummonBattery = tracked;
        if (_currentStep < StepPairs.Length)
            _currentStep++;
    }

    /// <summary>
    /// True when current battery matches the expected transition for the active step pair.
    /// </summary>
    public bool MatchesCurrentStep(int lastQueenBattery, int battery)
    {
        if (_currentStep >= StepPairs.Length)
            return false;

        var (from, to) = StepPairs[_currentStep];
        return lastQueenBattery == from && battery == to;
    }
}
