using System;
using Dalamud.Plugin.Services;
using Daedalus.Rotation.ArtemisCore.Helpers;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Reads the three assigned Battlehorn slots out of live game state.
///
/// <para>
/// This exists because ClientStructs <b>7.55.1.9047</b> added
/// <c>ActionManager.BeastmasterPets</c> — a <c>FixedSizeArray3&lt;byte&gt;</c> at 0x184 that
/// upstream documents as <i>"selected Battlehorn pets, XBMPet RowIds"</i>. Before that build there
/// was no way to read the roster at all, which is why
/// <see cref="ArtemisBattlehornState.IsPopulatedFromGame"/> exists to distinguish "unreadable"
/// from "empty".
/// </para>
///
/// <para>
/// <b>RowIds, not names.</b> The three bytes are XBMPet row ids. Turning one into a display name
/// and a <c>BeastClassification</c> needs the XBMPet sheet's columns, and the only sheet that names
/// them (<c>Lumina.Excel.Sheets.Experimental.XBMPet</c>) is marked evaluation-only and subject to
/// change — so this reader reports the ids truthfully and leaves the name blank rather than
/// inventing one. A slot with a real id and no name still tells the rotation the slot is occupied,
/// which is what the once-per-summon rules actually need.
/// </para>
/// </summary>
public sealed class BattlehornReader
{
    private readonly IPluginLog? _log;
    private bool _faulted;

    public BattlehornReader(IPluginLog? log = null) => _log = log;

    /// <summary>
    /// Fill <paramref name="state"/> from <c>ActionManager.BeastmasterPets</c>. Returns false when
    /// the read was not possible, in which case the state is left alone — so a transient failure
    /// during a zone change reports "unknown" rather than wrongly clearing a good roster.
    /// </summary>
    public unsafe bool TryRead(ArtemisBattlehornState state)
    {
        if (_faulted)
            return false;

        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.ActionManager.Instance();
            if (manager is null)
                return false;

            var pets = manager->BeastmasterPets;
            if (pets.Length < ArtemisBattlehornState.SlotCount)
                return false;

            state.SetRoster(
                MakeSlot(pets[0]),
                MakeSlot(pets[1]),
                MakeSlot(pets[2]));
            return true;
        }
        catch (Exception ex)
        {
            // One warning, then stop trying: a struct read that throws will throw every frame, and
            // a per-frame exception log is worse than the missing readout.
            _faulted = true;
            _log?.Warning(ex, "[Battlehorn] read failed — roster reporting disabled this session");
            return false;
        }
    }

    /// <summary>
    /// Whether the capture ledger already holds this beast. Pure convenience over
    /// <c>XBMManager.IsPetUnlocked</c>, which takes an XBMPet row id — the same ids this reader
    /// returns, so the two line up without a name lookup.
    /// </summary>
    public unsafe bool? IsPetUnlocked(uint petRowId)
    {
        if (petRowId == 0)
            return null;

        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.XBMManager.Instance();
            if (manager is null)
                return null;

            // DataState.Received == 3, NOT 2 (None=0, Requested=1, Received=3). Reading this as a
            // sequential enum would treat a fully-populated ledger as still loading.
            if (manager->State != FFXIVClientStructs.FFXIV.Client.Game.XBMManager.DataState.Received)
                return null;

            return manager->IsPetUnlocked(petRowId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>How many beasts are in the Master's Bestiary, or null when not yet loaded.</summary>
    public unsafe int? UnlockedCount()
    {
        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.XBMManager.Instance();
            if (manager is null)
                return null;

            return manager->State == FFXIVClientStructs.FFXIV.Client.Game.XBMManager.DataState.Received
                ? manager->NumUnlockedPets
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static BattlehornSlot MakeSlot(byte petRowId) =>
        petRowId == 0
            ? BattlehornSlot.Empty
            : new BattlehornSlot(petRowId, string.Empty, Daedalus.Data.BeastClassification.Unknown);
}
