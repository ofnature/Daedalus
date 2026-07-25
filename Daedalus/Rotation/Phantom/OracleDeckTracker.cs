using System.Collections.Generic;

namespace Daedalus.Rotation.Phantom;

/// <summary>
/// Tracks the Oracle prediction deck across one Predict window (RSR parity).
/// Predict opens a 4-card deck; the game rotates the offered card (a PredictionOf*
/// status) through the deck automatically. When the offered card changes without us
/// playing it, the previous card is discarded — on the last card there is nothing
/// to wait for, so it should be played unconditionally. Pure state, unit-testable.
/// </summary>
public sealed class OracleDeckTracker
{
    private readonly HashSet<uint> _remaining = [];
    private uint _currentCardActionId;

    /// <summary>Cards not yet seen-and-discarded this Predict window.</summary>
    public int RemainingCount => _remaining.Count;

    /// <summary>Call when Predict is dispatched — a fresh 4-card deck opens.</summary>
    public void OnPredictDispatched()
    {
        _remaining.Clear();
        _remaining.Add(41637); // Phantom Judgment
        _remaining.Add(41638); // Cleansing
        _remaining.Add(41639); // Blessing
        _remaining.Add(41640); // Starfall
        _currentCardActionId = 0;
    }

    /// <summary>
    /// Feed the currently offered card each frame (0 = none). When the offered card
    /// changes away from a previous one, that previous card was discarded.
    /// </summary>
    public void Update(uint activeCardActionId)
    {
        if (_currentCardActionId != 0 && activeCardActionId != _currentCardActionId)
            _remaining.Remove(_currentCardActionId);

        _currentCardActionId = activeCardActionId;
    }

    /// <summary>True when the given card is the only one left — play it, whatever it is.</summary>
    public bool IsLastCard(uint cardActionId)
        => _remaining.Count <= 1 && (_remaining.Count == 0 || _remaining.Contains(cardActionId));
}
