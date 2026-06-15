namespace Olympus.Services.Positional.Navigation;

/// <summary>
/// Coordinates when/where/safe/execute for positional vNav repositioning.
/// Fail-open: any constraint failure skips movement without stalling rotation.
/// </summary>
public interface IPositionalMovementService
{
    PositionalMovementState State { get; }

    void Update(in PositionalMovementUpdateRequest request);

    void Cancel(string reason);
}
