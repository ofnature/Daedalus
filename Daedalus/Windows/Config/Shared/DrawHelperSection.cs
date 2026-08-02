using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Localization;
using Daedalus.Windows;

namespace Daedalus.Windows.Config.Shared;

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

        ImGui.Spacing();

        // Treasure chest lines
        ImGui.Separator();
        ImGui.Text(Loc.T(LocalizedStrings.DrawHelper.TreasureLinesHeader, "Treasure Chests"));
        var showTreasure = dh.ShowTreasureLines;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ShowTreasureLines, "Show lines to treasure chests"), ref showTreasure)) { dh.ShowTreasureLines = showTreasure; save(); }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T(LocalizedStrings.DrawHelper.ShowTreasureLinesDesc,
                "Draws a line from you to every treasure coffer in range, with a ring at its base. Works anywhere coffers spawn."));
        }
        if (dh.ShowTreasureLines)
        {
            var treasureRange = dh.TreasureLineMaxDistance;
            if (ImGui.SliderFloat(Loc.T(LocalizedStrings.DrawHelper.TreasureLineMaxDistance, "Max Distance (y)"), ref treasureRange, 10f, 200f, "%.0f"))
            {
                dh.TreasureLineMaxDistance = treasureRange;
                save();
            }
            ColorPicker("Bronze", dh.BronzeChestLineColor, v => { dh.BronzeChestLineColor = v; save(); });
            ColorPicker("Silver", dh.SilverChestLineColor, v => { dh.SilverChestLineColor = v; save(); });
            ColorPicker("Gold", dh.GoldChestLineColor, v => { dh.GoldChestLineColor = v; save(); });
            ColorPicker("Unrecognised", dh.UnknownChestLineColor, v => { dh.UnknownChestLineColor = v; save(); });
        }

        var showCarrots = dh.ShowCarrotLines;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ShowCarrotLines, "Show lines to carrot spots"), ref showCarrots)) { dh.ShowCarrotLines = showCarrots; save(); }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T(LocalizedStrings.DrawHelper.ShowCarrotLinesDesc,
                "Occult Crescent only — the dig spots you use a Fortune Carrot on to raise a chest."));
        }
        if (dh.ShowCarrotLines)
        {
            var carrotRange = dh.CarrotLineMaxDistance;
            if (ImGui.SliderFloat(Loc.T(LocalizedStrings.DrawHelper.CarrotLineMaxDistance, "Max Distance (y)##carrot"), ref carrotRange, 10f, 200f, "%.0f"))
            {
                dh.CarrotLineMaxDistance = carrotRange;
                save();
            }
            ColorPicker("Line Color##carrot", dh.CarrotLineColor, v => { dh.CarrotLineColor = v; save(); });
        }

        var showMarked = dh.ShowMarkedMobLines;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.ShowMarkedMobLines, "Show lines to marked mobs"), ref showMarked)) { dh.ShowMarkedMobLines = showMarked; save(); }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T(LocalizedStrings.DrawHelper.ShowMarkedMobLinesDesc,
                "Draws a line to any mob the game itself has marked — quest targets, hunt bills, Occult quest-drop mobs. Unlike the chest lines, these stay visible in combat."));
        }
        if (dh.ShowMarkedMobLines)
        {
            var markedRange = dh.MarkedMobLineMaxDistance;
            if (ImGui.SliderFloat(Loc.T(LocalizedStrings.DrawHelper.MarkedMobLineMaxDistance, "Max Distance (y)##marked"), ref markedRange, 10f, 200f, "%.0f"))
            {
                dh.MarkedMobLineMaxDistance = markedRange;
                save();
            }
            ColorPicker("Line Color##marked", dh.MarkedMobLineColor, v => { dh.MarkedMobLineColor = v; save(); });
        }

        var labelObjects = dh.LabelWorldObjects;
        if (ImGui.Checkbox(Loc.T(LocalizedStrings.DrawHelper.LabelWorldObjects, "Debug: label nearby world objects"), ref labelObjects)) { dh.LabelWorldObjects = labelObjects; save(); }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(Loc.T(LocalizedStrings.DrawHelper.LabelWorldObjectsDesc,
                "Stamps the ObjectKind and name over every non-creature object within 30y. Use this to check what kind a chest reports if no line appears."));
        }
    }

    private static void ColorPicker(string label, uint currentColor, Action<uint> setter)
    {
        var c = ImGui.ColorConvertU32ToFloat4(currentColor);
        if (ImGui.ColorEdit4(label, ref c, ImGuiColorEditFlags.AlphaBar))
            setter(ImGui.ColorConvertFloat4ToU32(c));
    }
}
