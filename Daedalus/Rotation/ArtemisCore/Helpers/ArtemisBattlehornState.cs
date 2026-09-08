using Daedalus.Data;

namespace Daedalus.Rotation.ArtemisCore.Helpers;

/// <summary>
/// One assigned Battlehorn slot: which beast is on it and what classification it is.
/// Classification is what decides the Borrow action, so it travels with the slot.
/// </summary>
/// <param name="PactNameId">The beast's NameId; 0 for an empty slot.</param>
/// <param name="Name">Display name, for logs and the debug panel.</param>
/// <param name="Classification">Decides which Borrow the beast can lend.</param>
public readonly record struct BattlehornSlot(
    uint PactNameId,
    string Name,
    BeastClassification Classification)
{
    public bool IsEmpty => PactNameId == 0;

    public static readonly BattlehornSlot Empty = new(0, string.Empty, BeastClassification.Unknown);
}

/// <summary>
/// The three assigned Battlehorns, which one is active, and the once-per-summon budget.
///
/// <para>
/// STUB, and honestly so. The slot model, the once-per-summon rule for Tempered Release and
/// Borrow, and the fact that a swap resets that budget are all knowable from the job's design —
/// those are implemented. What is NOT knowable yet is how any of it is <b>read from the game</b>:
/// the Beastmaster gauge struct is not in the published ClientStructs, so nothing populates this
/// from live state. <see cref="IsPopulatedFromGame"/> says so out loud rather than letting a
/// permanently-empty roster read as "no beasts assigned".
/// </para>
///
/// <para>
/// When the gauge lands, the only change needed here is a reader that fills the three slots and
/// the active index each frame. Every rule below already works off that state.
/// </para>
/// </summary>
public sealed class ArtemisBattlehornState
{
    /// <summary>Beastmaster carries three assigned Battlehorns; one is active in combat.</summary>
    public const int SlotCount = 3;

    /// <summary>
    /// False until a live gauge reader exists. Any consumer that would otherwise report "no
    /// beasts" must check this first and report "unknown" instead — an empty roster and an
    /// unreadable one are different states and only one of them is the player's problem.
    /// </summary>
    public bool IsPopulatedFromGame { get; private set; }

    private readonly BattlehornSlot[] _slots =
        [BattlehornSlot.Empty, BattlehornSlot.Empty, BattlehornSlot.Empty];

    private int _activeIndex = -1;

    /// <summary>The slot currently summoned, or null when none is out.</summary>
    public BattlehornSlot? Active =>
        _activeIndex >= 0 && _activeIndex < SlotCount && !_slots[_activeIndex].IsEmpty
            ? _slots[_activeIndex]
            : null;

    /// <summary>Read a slot by index.</summary>
    public BattlehornSlot this[int index] =>
        index >= 0 && index < SlotCount ? _slots[index] : BattlehornSlot.Empty;

    /// <summary>Tempered Release is once per summon; true when this summon has not spent it.</summary>
    public bool TemperedReleaseAvailable { get; private set; }

    /// <summary>Borrow is once per summon; true when this summon has not spent it.</summary>
    public bool BorrowAvailable { get; private set; }

    /// <summary>
    /// Fill the roster from live game state. Not called by anything yet — see the class remarks.
    /// </summary>
    public void SetRoster(BattlehornSlot slot0, BattlehornSlot slot1, BattlehornSlot slot2)
    {
        _slots[0] = slot0;
        _slots[1] = slot1;
        _slots[2] = slot2;
        IsPopulatedFromGame = true;
    }

    /// <summary>
    /// A beast came out. Resets the once-per-summon budget, which is the whole reason a swap is
    /// a rotational decision rather than bookkeeping.
    /// </summary>
    public void OnSummoned(int slotIndex)
    {
        _activeIndex = slotIndex;
        TemperedReleaseAvailable = true;
        BorrowAvailable = true;
    }

    /// <summary>The beast left — Parting Blow, a swap, or the pact ending.</summary>
    public void OnRetreated()
    {
        _activeIndex = -1;
        TemperedReleaseAvailable = false;
        BorrowAvailable = false;
    }

    /// <summary>Mark Tempered Release spent for this summon.</summary>
    public void MarkTemperedReleaseUsed() => TemperedReleaseAvailable = false;

    /// <summary>Mark Borrow spent for this summon.</summary>
    public void MarkBorrowUsed() => BorrowAvailable = false;

    /// <summary>Combat end / zone change.</summary>
    public void Reset()
    {
        _slots[0] = _slots[1] = _slots[2] = BattlehornSlot.Empty;
        _activeIndex = -1;
        TemperedReleaseAvailable = false;
        BorrowAvailable = false;
        IsPopulatedFromGame = false;
    }

    /// <summary>One-line readout for the debug panel; distinguishes unreadable from empty.</summary>
    public string Describe()
    {
        if (!IsPopulatedFromGame)
            return "Battlehorns: unknown (no gauge reader — BST data unpublished)";

        var active = Active is { } a ? $"{a.Name} ({a.Classification})" : "none out";
        return $"Battlehorns: {active}"
             + $", Tempered {(TemperedReleaseAvailable ? "ready" : "spent")}"
             + $", Borrow {(BorrowAvailable ? "ready" : "spent")}";
    }
}
