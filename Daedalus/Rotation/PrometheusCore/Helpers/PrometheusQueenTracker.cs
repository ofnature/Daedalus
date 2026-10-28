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
        (80, 100),
        (100, 50),
        (50, 60),
    ];

    private int _currentStep;
    private bool _wasQueenActive;

    public int CurrentStep => _currentStep;

    public void Reset(bool isQueenActive = false)
    {
        _currentStep = 0;
        _wasQueenActive = isQueenActive;
    }

    /// <summary>
    /// Advances the step once per Queen summon (the frame the Queen comes out). The gauge's LastQueenBattery
    /// keeps its value between pulls and can repeat, so a change in it is not a summon (RSR 8153eea2).
    /// </summary>
    public void OnFrame(bool isQueenActive)
    {
        if (isQueenActive && !_wasQueenActive && _currentStep < StepPairs.Length)
            _currentStep++;
        _wasQueenActive = isQueenActive;
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
