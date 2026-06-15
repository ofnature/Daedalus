namespace Olympus.Services.Positional.Navigation;

/// <summary>
/// Tri-state safety for a stand position. Callers treat <see cref="Unknown"/> as
/// <see cref="Safe"/> (fail-open) per burn-reference rules.
/// </summary>
public enum PositionSafety
{
    Safe,
    Unsafe,
    Imminent,
    Unknown,
}
