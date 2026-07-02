using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.ProteusCore.Abilities;

/// <summary>
/// Declarative <see cref="AbilityBehavior"/> for the Blue Mage kit. BLU spells that aren't SLOTTED
/// in the active set are rejected by the scheduler's dispatch-time status gate, so a push is only a
/// candidate — unslotted spells fall through to the next priority instead of stalling.
/// </summary>
public static class ProteusAbilities
{
    // --- Fillers ---
    public static readonly AbilityBehavior SonicBoom = new() { Action = BLUActions.SonicBoom };
    public static readonly AbilityBehavior WaterCannon = new() { Action = BLUActions.WaterCannon };
    public static readonly AbilityBehavior GoblinPunch = new() { Action = BLUActions.GoblinPunch };
    public static readonly AbilityBehavior Plaincracker = new() { Action = BLUActions.Plaincracker, Toggle = cfg => cfg.BlueMage.EnableAoERotation };

    // --- Nukes / DoT ---
    public static readonly AbilityBehavior TheRoseOfDestruction = new() { Action = BLUActions.TheRoseOfDestruction, Toggle = cfg => cfg.BlueMage.EnableRoseOfDestruction };
    public static readonly AbilityBehavior SongOfTorment = new() { Action = BLUActions.SongOfTorment, Toggle = cfg => cfg.BlueMage.EnableSongOfTorment };

    // --- Role / utility ---
    public static readonly AbilityBehavior AethericMimicry = new() { Action = BLUActions.AethericMimicry, Toggle = cfg => cfg.BlueMage.EnableMimicry };
    public static readonly AbilityBehavior MightyGuard = new() { Action = BLUActions.MightyGuard, Toggle = cfg => cfg.BlueMage.EnableMightyGuard };
    public static readonly AbilityBehavior Diamondback = new() { Action = BLUActions.Diamondback, Toggle = cfg => cfg.BlueMage.EnableDiamondback };
    public static readonly AbilityBehavior WhiteWind = new() { Action = BLUActions.WhiteWind, Toggle = cfg => cfg.BlueMage.EnableWhiteWind };
}
