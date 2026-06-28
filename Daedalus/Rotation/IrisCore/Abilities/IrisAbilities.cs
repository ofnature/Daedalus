using Daedalus.Data;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Rotation.IrisCore.Context;
using Daedalus.Rotation.IrisCore.Helpers;

namespace Daedalus.Rotation.IrisCore.Abilities;

/// <summary>
/// Declarative <see cref="AbilityBehavior"/> for every ability the Pictomancer rotation fires.
/// </summary>
public static class IrisAbilities
{
    // --- Base combo (single-target) ---
    public static readonly AbilityBehavior FireInRed = new()
    {
        Action = PCTActions.FireInRed,
        AdjustedActionProbe = PCTActions.FireInRed.ActionId,
    };
    public static readonly AbilityBehavior AeroInGreen = new()
    {
        Action = PCTActions.AeroInGreen,
        AdjustedActionProbe = PCTActions.FireInRed.ActionId,
        ReplacementBaseId = PCTActions.FireInRed.ActionId,
    };
    public static readonly AbilityBehavior WaterInBlue = new()
    {
        Action = PCTActions.WaterInBlue,
        AdjustedActionProbe = PCTActions.FireInRed.ActionId,
        ReplacementBaseId = PCTActions.FireInRed.ActionId,
    };

    // --- Subtractive combo (single-target) ---
    public static readonly AbilityBehavior BlizzardInCyan = new()
    {
        Action = PCTActions.BlizzardInCyan,
        Toggle = cfg => cfg.Pictomancer.EnableSubtractiveCombo,
        AdjustedActionProbe = PCTActions.BlizzardInCyan.ActionId,
    };
    public static readonly AbilityBehavior StoneInYellow = new()
    {
        Action = PCTActions.StoneInYellow,
        Toggle = cfg => cfg.Pictomancer.EnableSubtractiveCombo,
        AdjustedActionProbe = PCTActions.BlizzardInCyan.ActionId,
        ReplacementBaseId = PCTActions.BlizzardInCyan.ActionId,
    };
    public static readonly AbilityBehavior ThunderInMagenta = new()
    {
        Action = PCTActions.ThunderInMagenta,
        Toggle = cfg => cfg.Pictomancer.EnableSubtractiveCombo,
        // Probe the combo STARTER (Blizzard in Cyan) — only it morphs through the chain. StoneInYellow
        // never morphs, so this step's gate never passed and the ST subtractive stalled at Thunder (the
        // same bug as the AoE Thunder II step). Matches Stone in Yellow + the AoE Thunder II behavior.
        AdjustedActionProbe = PCTActions.BlizzardInCyan.ActionId,
        ReplacementBaseId = PCTActions.BlizzardInCyan.ActionId,
    };

    // --- AoE base combo ---
    public static readonly AbilityBehavior Fire2InRed = new()
    {
        Action = PCTActions.Fire2InRed,
        Toggle = cfg => cfg.Pictomancer.EnableAoERotation,
        AdjustedActionProbe = PCTActions.Fire2InRed.ActionId,
    };
    public static readonly AbilityBehavior Aero2InGreen = new()
    {
        Action = PCTActions.Aero2InGreen,
        Toggle = cfg => cfg.Pictomancer.EnableAoERotation,
        AdjustedActionProbe = PCTActions.Fire2InRed.ActionId,
        ReplacementBaseId = PCTActions.Fire2InRed.ActionId,
    };
    public static readonly AbilityBehavior Water2InBlue = new()
    {
        Action = PCTActions.Water2InBlue,
        Toggle = cfg => cfg.Pictomancer.EnableAoERotation,
        AdjustedActionProbe = PCTActions.Fire2InRed.ActionId,
        ReplacementBaseId = PCTActions.Fire2InRed.ActionId,
    };

    // --- AoE subtractive ---
    public static readonly AbilityBehavior Blizzard2InCyan = new()
    {
        Action = PCTActions.Blizzard2InCyan,
        Toggle = cfg => cfg.Pictomancer.EnableAoERotation,
        AdjustedActionProbe = PCTActions.Blizzard2InCyan.ActionId,
    };
    public static readonly AbilityBehavior Stone2InYellow = new()
    {
        Action = PCTActions.Stone2InYellow,
        Toggle = cfg => cfg.Pictomancer.EnableAoERotation,
        AdjustedActionProbe = PCTActions.Blizzard2InCyan.ActionId,
        ReplacementBaseId = PCTActions.Blizzard2InCyan.ActionId,
    };
    public static readonly AbilityBehavior Thunder2InMagenta = new()
    {
        Action = PCTActions.Thunder2InMagenta,
        Toggle = cfg => cfg.Pictomancer.EnableAoERotation,
        AdjustedActionProbe = PCTActions.Blizzard2InCyan.ActionId,
        ReplacementBaseId = PCTActions.Blizzard2InCyan.ActionId,
    };

    // --- Paint spenders ---
    public static readonly AbilityBehavior HolyInWhite = new() { Action = PCTActions.HolyInWhite, Toggle = cfg => cfg.Pictomancer.EnableHolyInWhite };
    public static readonly AbilityBehavior CometInBlack = new() { Action = PCTActions.CometInBlack, Toggle = cfg => cfg.Pictomancer.EnableCometInBlack };

    // --- Motifs ---
    public static readonly AbilityBehavior PomMotif = new()
    {
        Action = PCTActions.PomMotif,
        Toggle = cfg => cfg.Pictomancer.EnableCreatureMotif && cfg.Pictomancer.EnablePomMotif,
        AdjustedActionProbe = PCTActions.CreatureMotif.ActionId,
    };
    public static readonly AbilityBehavior WingMotif = new()
    {
        Action = PCTActions.WingMotif,
        Toggle = cfg => cfg.Pictomancer.EnableCreatureMotif && cfg.Pictomancer.EnableWingMotif,
        AdjustedActionProbe = PCTActions.CreatureMotif.ActionId,
    };
    public static readonly AbilityBehavior ClawMotif = new()
    {
        Action = PCTActions.ClawMotif,
        Toggle = cfg => cfg.Pictomancer.EnableCreatureMotif && cfg.Pictomancer.EnableClawMotif,
        AdjustedActionProbe = PCTActions.CreatureMotif.ActionId,
    };
    public static readonly AbilityBehavior MawMotif = new()
    {
        Action = PCTActions.MawMotif,
        Toggle = cfg => cfg.Pictomancer.EnableCreatureMotif && cfg.Pictomancer.EnableMawMotif,
        AdjustedActionProbe = PCTActions.CreatureMotif.ActionId,
    };
    public static readonly AbilityBehavior HammerMotif = new()
    {
        Action = PCTActions.HammerMotif,
        Toggle = cfg => cfg.Pictomancer.EnableWeaponMotif && cfg.Pictomancer.EnableHammerMotif,
        AdjustedActionProbe = PCTActions.WeaponMotif.ActionId,
    };
    public static readonly AbilityBehavior StarrySkyMotif = new()
    {
        Action = PCTActions.StarrySkyMotif,
        Toggle = cfg => cfg.Pictomancer.EnableLandscapeMotif && cfg.Pictomancer.EnableStarrySkyMotif,
        AdjustedActionProbe = PCTActions.LandscapeMotif.ActionId,
    };

    private static readonly ChargeHoldPolicy LivingMuseBurstHold = ChargeHoldPolicy.HoldOneForBurst(ctx =>
    {
        if (ctx is not IIrisContext iris) return true;
        if (!iris.Configuration.Pictomancer.EnableBurstPooling) return true;
        return IrisBurstHelper.IsLivingMuseInBurst(iris);
    });

    // --- Living Muses ---
    public static readonly AbilityBehavior PomMuse = new()
    {
        Action = PCTActions.PomMuse,
        Toggle = cfg => cfg.Pictomancer.EnableLivingMuse,
        AdjustedActionProbe = PCTActions.LivingMuse.ActionId,
        ChargeSource = PCTActions.LivingMuse.ActionId,
        ChargeHold = LivingMuseBurstHold,
    };
    public static readonly AbilityBehavior WingedMuse = new()
    {
        Action = PCTActions.WingedMuse,
        Toggle = cfg => cfg.Pictomancer.EnableLivingMuse,
        AdjustedActionProbe = PCTActions.LivingMuse.ActionId,
        ChargeSource = PCTActions.LivingMuse.ActionId,
        ChargeHold = LivingMuseBurstHold,
    };
    public static readonly AbilityBehavior ClawedMuse = new()
    {
        Action = PCTActions.ClawedMuse,
        Toggle = cfg => cfg.Pictomancer.EnableLivingMuse,
        AdjustedActionProbe = PCTActions.LivingMuse.ActionId,
        ChargeSource = PCTActions.LivingMuse.ActionId,
        ChargeHold = LivingMuseBurstHold,
    };
    public static readonly AbilityBehavior FangedMuse = new()
    {
        Action = PCTActions.FangedMuse,
        Toggle = cfg => cfg.Pictomancer.EnableLivingMuse,
        AdjustedActionProbe = PCTActions.LivingMuse.ActionId,
        ChargeSource = PCTActions.LivingMuse.ActionId,
        ChargeHold = LivingMuseBurstHold,
    };

    // --- Steel/Scenic Muses & Starry Muse ---
    public static readonly AbilityBehavior StrikingMuse = new()
    {
        Action = PCTActions.StrikingMuse,
        Toggle = cfg => cfg.Pictomancer.EnableSteelMuse,
        AdjustedActionProbe = PCTActions.SteelMuse.ActionId,
    };
    public static readonly AbilityBehavior StarryMuse = new()
    {
        Action = PCTActions.StarryMuse,
        Toggle = cfg => cfg.Pictomancer.EnableStarryMuse,
        AdjustedActionProbe = PCTActions.ScenicMuse.ActionId,
    };

    // --- Hammer combo ---
    public static readonly AbilityBehavior HammerStamp = new() { Action = PCTActions.HammerStamp };
    public static readonly AbilityBehavior HammerBrush = new()
    {
        Action = PCTActions.HammerBrush,
        AdjustedActionProbe = PCTActions.HammerStamp.ActionId,
        ReplacementBaseId = PCTActions.HammerStamp.ActionId,
    };
    public static readonly AbilityBehavior PolishingHammer = new()
    {
        Action = PCTActions.PolishingHammer,
        AdjustedActionProbe = PCTActions.HammerStamp.ActionId,
        ReplacementBaseId = PCTActions.HammerStamp.ActionId,
    };

    // --- Portraits ---
    public static readonly AbilityBehavior MogOfTheAges = new() { Action = PCTActions.MogOfTheAges, Toggle = cfg => cfg.Pictomancer.EnablePortraits };
    public static readonly AbilityBehavior RetributionOfTheMadeen = new() { Action = PCTActions.RetributionOfTheMadeen, Toggle = cfg => cfg.Pictomancer.EnablePortraits };

    // --- Rainbow Drip / Star Prism ---
    public static readonly AbilityBehavior RainbowDrip = new() { Action = PCTActions.RainbowDrip, Toggle = cfg => cfg.Pictomancer.EnableRainbowDrip };
    public static readonly AbilityBehavior StarPrism = new() { Action = PCTActions.StarPrism, Toggle = cfg => cfg.Pictomancer.EnableStarPrism };

    // --- Other oGCDs ---
    public static readonly AbilityBehavior SubtractivePalette = new() { Action = PCTActions.SubtractivePalette, Toggle = cfg => cfg.Pictomancer.EnableSubtractivePalette };
    public static readonly AbilityBehavior TemperaCoat = new() { Action = PCTActions.TemperaCoat, Toggle = cfg => cfg.Pictomancer.EnableTemperaCoat };
    public static readonly AbilityBehavior TemperaGrassa = new() { Action = PCTActions.TemperaGrassa, Toggle = cfg => cfg.Pictomancer.EnableTemperaGrassa };
    public static readonly AbilityBehavior Smudge = new() { Action = PCTActions.Smudge, Toggle = cfg => cfg.Pictomancer.EnableSmudge };

    // --- Role ---
    public static readonly AbilityBehavior Swiftcast = new() { Action = RoleActions.Swiftcast };
    public static readonly AbilityBehavior LucidDreaming = new() { Action = RoleActions.LucidDreaming, Toggle = cfg => cfg.CasterShared.EnableLucidDreaming };
    public static readonly AbilityBehavior Addle = new() { Action = RoleActions.Addle, Toggle = cfg => cfg.Pictomancer.EnableAddle };
}
