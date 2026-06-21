using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Olympus.Localization;
using Olympus.Windows;

namespace Olympus.Windows.Config.Shared;

/// <summary>
/// Config section for Draw Helper — world-space visual overlays.
/// </summary>
public sealed class DrawHelperSection
{
    private readonly Configuration config;
    private readonly Action save;

    public DrawHelperSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Draw()
    {
        var dh = config.DrawHelper;

        // Master toggle
        ImGui.Separator();
        ImGui.Text(Loc.T(LocalizedStrings.DrawHelper.SectionTitle, "Draw Helper"));
        var drawingEnabled = dh.DrawingEnabled;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.EnableDrawing, "Enable Drawing"), ref drawingEnabled)) { dh.DrawingEnabled = drawingEnabled; save(); }

        if (!dh.DrawingEnabled)
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.DrawHelper.EnableDrawingDisabledHint, "Enable drawing to configure options below."));
            return;
        }

        ImGui.Spacing();

        // Pictomancy backend
        ImGui.Separator();
        ImGui.Text(Loc.T(LocalizedStrings.DrawHelper.RenderingHeader, "Rendering"));
        var usePicto = dh.UsePictomancy;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.UsePictomancy, "Use Pictomancy (3D rendering)"), ref usePicto)) { dh.UsePictomancy = usePicto; save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T(LocalizedStrings.DrawHelper.UsePictomancyTooltip, "Uses bundled Pictomancy for 3D overlays. When disabled or unavailable, Draw Helper falls back to a 2D screen projection."));

        var alpha = dh.PictomancyMaxAlpha;
        if (ImGui.SliderFloat(Loc.T(LocalizedStrings.DrawHelper.MaxAlpha, "Max Alpha"), ref alpha, 0.1f, 1f, "%.2f")) { dh.PictomancyMaxAlpha = alpha; save(); }

        var clipUi = dh.PictomancyClipNativeUI;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ClipToGameUI, "Clip to game UI"), ref clipUi)) { dh.PictomancyClipNativeUI = clipUi; save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Loc.T(LocalizedStrings.DrawHelper.ClipToGameUITooltip, "Hides overlays behind the cast bar and other native UI. May break after game patches; Draw Helper disables it automatically if Pictomancy throws."));

        ImGui.Spacing();

        // Enemy hitboxes
        ImGui.Separator();
        ImGui.Text(Loc.T(LocalizedStrings.DrawHelper.EnemyHitboxesHeader, "Enemy Hitboxes"));
        var showHitboxes = dh.ShowEnemyHitboxes;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ShowEnemyHitboxes, "Show enemy hitboxes"), ref showHitboxes)) { dh.ShowEnemyHitboxes = showHitboxes; save(); }
        if (dh.ShowEnemyHitboxes)
            ColorPicker("Hitbox Color", dh.EnemyHitboxColor, v => { dh.EnemyHitboxColor = v; save(); });

        ImGui.Spacing();

        // Melee range
        ImGui.Separator();
        ImGui.Text(Loc.T(LocalizedStrings.DrawHelper.MeleeRangeHeader, "Melee Range"));
        var showMelee = dh.ShowMeleeRange;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ShowMeleeRange, "Show melee range at target"), ref showMelee)) { dh.ShowMeleeRange = showMelee; save(); }
        if (dh.ShowMeleeRange)
        {
            var fade = dh.MeleeRangeFade;
            if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.FadeWhenInRange, "Fade when in range"), ref fade)) { dh.MeleeRangeFade = fade; save(); }
            ColorPicker("In Range", dh.MeleeRangeColor, v => { dh.MeleeRangeColor = v; save(); });
            ColorPicker("Out of Range", dh.MeleeRangeOutOfRangeColor, v => { dh.MeleeRangeOutOfRangeColor = v; save(); });
        }

        ImGui.Spacing();

        // Ranged range
        ImGui.Separator();
        ImGui.Text(Loc.T(LocalizedStrings.DrawHelper.RangedRangeHeader, "Ranged Range"));
        var showRanged = dh.ShowRangedRange;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ShowRangedRange, "Show ranged range at target"), ref showRanged)) { dh.ShowRangedRange = showRanged; save(); }
        if (dh.ShowRangedRange)
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.DrawHelper.RangedRangeAutoDetect, "Auto-detects 25y range for all ranged/caster jobs."));
            ColorPicker("In Range##ranged", dh.RangedRangeColor, v => { dh.RangedRangeColor = v; save(); });
            ColorPicker("Out of Range##ranged", dh.RangedRangeOutOfRangeColor, v => { dh.RangedRangeOutOfRangeColor = v; save(); });
        }

        ImGui.Spacing();

        // Positionals
        ImGui.Separator();
        ImGui.Text(Loc.T(LocalizedStrings.DrawHelper.PositionalsHeader, "Positionals"));
        var showPos = dh.ShowPositionals;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ShowPositionals, "Show positional zones at target"), ref showPos)) { dh.ShowPositionals = showPos; save(); }
        if (dh.ShowPositionals)
        {
            ColorPicker("Rear", dh.PositionalRearColor, v => { dh.PositionalRearColor = v; save(); });
            ColorPicker("Flank", dh.PositionalFlankColor, v => { dh.PositionalFlankColor = v; save(); });
        }

        ImGui.Spacing();

        // Astrologian card range
        ImGui.Separator();
        ImGui.Text(Loc.T(LocalizedStrings.DrawHelper.AstCardRangeHeader, "Astrologian Card Range"));
        var showAstCards = dh.ShowAstCardRange;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ShowAstCardRange, "Show card range (30y on self)"), ref showAstCards))
        {
            dh.ShowAstCardRange = showAstCards;
            save();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T(LocalizedStrings.DrawHelper.AstCardRangeDesc,
                "Draws a 30y ring on you for Balance/Spear and support cards. Green/red markers on allies in/out of range."));
        }
        if (dh.ShowAstCardRange)
        {
            ColorPicker("Range Ring", dh.AstCardRangeColor, v => { dh.AstCardRangeColor = v; save(); });
            ColorPicker("Range Fill", dh.AstCardRangeFillColor, v => { dh.AstCardRangeFillColor = v; save(); });
            ColorPicker("Ally In Range", dh.AstCardAllyInRangeColor, v => { dh.AstCardAllyInRangeColor = v; save(); });
            ColorPicker("Ally Out of Range", dh.AstCardAllyOutOfRangeColor, v => { dh.AstCardAllyOutOfRangeColor = v; save(); });
        }

    }

    private static void ColorPicker(string label, uint currentColor, Action<uint> setter)
    {
        var c = ImGui.ColorConvertU32ToFloat4(currentColor);
        if (ImGui.ColorEdit4(label, ref c, ImGuiColorEditFlags.AlphaBar))
            setter(ImGui.ColorConvertFloat4ToU32(c));
    }
}
