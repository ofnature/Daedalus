using System;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Rotation.ArtemisCore.Helpers;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Copies the Beastmaster job gauge out of the game.
/// <para>
/// <c>JobGaugeManager.Beastmaster</c> exists from ClientStructs <c>78975d83</c> (Dalamud 15.0.3.7). The
/// gauge area is a union shared by every job, so this must only be called while the player IS a
/// Beastmaster — on any other job the same bytes are that job's gauge.
/// </para>
/// </summary>
public sealed class BeastmasterGaugeReader
{
    private readonly IPluginLog? _log;
    private bool _faulted;

    public BeastmasterGaugeReader(IPluginLog? log = null) => _log = log;

    /// <summary>
    /// The gauge this frame, or null when it cannot be read — in which case Artemis falls back to what it
    /// did before the gauge existed, rather than acting on zeroes.
    /// </summary>
    public unsafe ArtemisGauge? TryRead()
    {
        if (_faulted)
            return null;

        try
        {
            var manager = FFXIVClientStructs.FFXIV.Client.Game.JobGaugeManager.Instance();
            if (manager is null)
                return null;

            var gauge = manager->Beastmaster;
            return new ArtemisGauge(
                Tp: gauge.TPGauge,
                PetTp: gauge.PetTPGauge,
                PetTpAtLastUse: gauge.PetTPAtLastUse,
                ActiveBattlehorn: gauge.ActiveBattlehorn,
                ComboState: gauge.InstinctualComboState,
                Affinity: ToAffinity((byte)gauge.CurrentAffinity),
                ChainCount: gauge.ChainCount,
                KinshipBattlehorn: gauge.KinshipBattlehorn,
                KinshipClassification: gauge.KinshipClassification,
                NaturalInstinct: gauge.NaturalInstinct,
                MasteredInstinct: gauge.MasteredInstinct);
        }
        catch (Exception ex)
        {
            // One warning, then stop: a struct read that throws will throw every frame.
            _faulted = true;
            _log?.Warning(ex, "[Beastmaster] gauge read failed — falling back to inference this session");
            return null;
        }
    }

    /// <summary>
    /// The game's <c>BeastmasterAffinity</c> and our <see cref="InstinctAffinity"/> use the same numbers
    /// (None 0, Volant 1, Rampant 2, Durant 3, Eldritch 4, Sunstrider 5, Moonstalker 6). Anything outside
    /// that range is a value neither of us knows, and reads as None rather than as a made-up affinity.
    /// </summary>
    public static InstinctAffinity ToAffinity(byte raw)
        => raw <= (byte)InstinctAffinity.Moonstalker ? (InstinctAffinity)raw : InstinctAffinity.None;
}
