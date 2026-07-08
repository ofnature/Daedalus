using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;

namespace Daedalus.Rotation.NikeCore.Abilities;

/// <summary>
/// Declarative <see cref="AbilityBehavior"/> for every ability the Samurai
/// rotation fires.
/// </summary>
public static class NikeAbilities
{
    // --- Ranged filler ---
    // Plain-GCD dagger-toss equivalent: keep GCD uptime when out of melee range instead of
    // idling until we walk back in (NIN Throwing Dagger parity). No Toggle — uptime filler is
    // never something to switch off; the module gates it purely on range + level + readiness.
    public static readonly AbilityBehavior Enpi = new() { Action = SAMActions.Enpi };

    // --- Combo starters ---
    public static readonly AbilityBehavior Hakaze = new() { Action = SAMActions.Hakaze };
    public static readonly AbilityBehavior Gyofu = new() { Action = SAMActions.Gyofu };
    public static readonly AbilityBehavior Fuga = new() { Action = SAMActions.Fuga, Toggle = cfg => cfg.Samurai.EnableAoERotation };
    public static readonly AbilityBehavior Fuko = new() { Action = SAMActions.Fuko, Toggle = cfg => cfg.Samurai.EnableAoERotation };

    // --- Combo step 2 ---
    public static readonly AbilityBehavior Jinpu = new() { Action = SAMActions.Jinpu };
    public static readonly AbilityBehavior Shifu = new() { Action = SAMActions.Shifu };
    public static readonly AbilityBehavior Yukikaze = new() { Action = SAMActions.Yukikaze };

    // --- Combo finishers / Meikyo ---
    public static readonly AbilityBehavior Gekko = new() { Action = SAMActions.Gekko };
    public static readonly AbilityBehavior Kasha = new() { Action = SAMActions.Kasha };
    public static readonly AbilityBehavior Mangetsu = new() { Action = SAMActions.Mangetsu, Toggle = cfg => cfg.Samurai.EnableAoERotation };
    public static readonly AbilityBehavior Oka = new() { Action = SAMActions.Oka, Toggle = cfg => cfg.Samurai.EnableAoERotation };

    // --- Iaijutsu ---
    public static readonly AbilityBehavior Higanbana = new() { Action = SAMActions.Higanbana, Toggle = cfg => cfg.Samurai.EnableIaijutsu };
    public static readonly AbilityBehavior TenkaGoken = new() { Action = SAMActions.TenkaGoken, Toggle = cfg => cfg.Samurai.EnableIaijutsu };
    public static readonly AbilityBehavior MidareSetsugekka = new() { Action = SAMActions.MidareSetsugekka, Toggle = cfg => cfg.Samurai.EnableIaijutsu };

    // --- Tendo (Lv.100) Kaeshi — slot resolution only; burst logic Phase C ---
    // No ProcBuff on any Kaeshi below — TryPushTsubameGaeshi already gates on the dual
    // adjusted-action / status check (TsubameGaeshiActionReady). A second ProcBuff gate in
    // the scheduler re-creates the stale-frame race that drops the Kaeshi after Iaijutsu.
    public static readonly AbilityBehavior TendoKaeshiGoken = new()
    {
        Action = SAMActions.TendoKaeshiGoken,
        Toggle = cfg => cfg.Samurai.EnableTsubamegaeshi,
    };
    public static readonly AbilityBehavior TendoKaeshiSetsugekka = new()
    {
        Action = SAMActions.TendoKaeshiSetsugekka,
        Toggle = cfg => cfg.Samurai.EnableTsubamegaeshi,
    };

    // --- Tsubame-gaeshi ---
    public static readonly AbilityBehavior KaeshiHiganbana = new()
    {
        Action = SAMActions.KaeshiHiganbana,
        Toggle = cfg => cfg.Samurai.EnableTsubamegaeshi,
    };
    public static readonly AbilityBehavior KaeshiGoken = new()
    {
        Action = SAMActions.KaeshiGoken,
        Toggle = cfg => cfg.Samurai.EnableTsubamegaeshi,
    };
    public static readonly AbilityBehavior KaeshiSetsugekka = new()
    {
        Action = SAMActions.KaeshiSetsugekka,
        Toggle = cfg => cfg.Samurai.EnableTsubamegaeshi,
    };

    // --- Ogi Namikiri / Kaeshi: Namikiri / Zanshin ---
    public static readonly AbilityBehavior OgiNamikiri = new()
    {
        Action = SAMActions.OgiNamikiri,
        Toggle = cfg => cfg.Samurai.EnableOgiNamikiri,
        ProcBuff = SAMActions.StatusIds.OgiNamikiriReady,
    };
    public static readonly AbilityBehavior KaeshiNamikiri = new()
    {
        Action = SAMActions.KaeshiNamikiri,
        // No ProcBuff here — module gate (KaeshiNamikiriReady) already covers the dual
        // adjusted-action / status-2960 check. A second ProcBuff gate in the scheduler
        // creates a stale-frame race after Ogi Namikiri fires.
    };
    public static readonly AbilityBehavior Zanshin = new()
    {
        Action = SAMActions.Zanshin,
        Toggle = cfg => cfg.Samurai.EnableZanshin,
        ProcBuff = SAMActions.StatusIds.ZanshinReady,
    };

    // --- Kenki spenders ---
    public static readonly AbilityBehavior Shinten = new() { Action = SAMActions.Shinten, Toggle = cfg => cfg.Samurai.EnableShinten };
    public static readonly AbilityBehavior Kyuten = new() { Action = SAMActions.Kyuten, Toggle = cfg => cfg.Samurai.EnableKyuten };
    public static readonly AbilityBehavior Senei = new() { Action = SAMActions.Senei, Toggle = cfg => cfg.Samurai.EnableSenei };
    public static readonly AbilityBehavior Guren = new() { Action = SAMActions.Guren, Toggle = cfg => cfg.Samurai.EnableGuren };

    // --- Buffs / Meditation ---
    public static readonly AbilityBehavior Shoha = new() { Action = SAMActions.Shoha, Toggle = cfg => cfg.Samurai.EnableShoha };
    public static readonly AbilityBehavior MeikyoShisui = new() { Action = SAMActions.MeikyoShisui, Toggle = cfg => cfg.Samurai.EnableMeikyoShisui };
    public static readonly AbilityBehavior Ikishoten = new() { Action = SAMActions.Ikishoten, Toggle = cfg => cfg.Samurai.EnableIkishoten };

    // --- Role ---
    public static readonly AbilityBehavior SecondWind = new() { Action = RoleActions.SecondWind, Toggle = cfg => cfg.MeleeShared.EnableSecondWind };
    public static readonly AbilityBehavior Bloodbath = new() { Action = RoleActions.Bloodbath, Toggle = cfg => cfg.MeleeShared.EnableBloodbath };
    public static readonly AbilityBehavior TrueNorth = new() { Action = RoleActions.TrueNorth, Toggle = cfg => cfg.MeleeShared.EnableTrueNorth };
    public static readonly AbilityBehavior Feint = new() { Action = RoleActions.Feint, Toggle = cfg => cfg.Samurai.EnableFeint };
}
