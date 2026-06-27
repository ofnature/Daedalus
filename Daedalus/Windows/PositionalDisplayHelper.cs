using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Localization;
using Daedalus.Rotation.Common;
using Daedalus.Services.Positional;

namespace Daedalus.Windows;

/// <summary>
/// Shared positional indicator for MainWindow and Overlay — shows required vs current like SAM canvas logic.
/// </summary>
internal static class PositionalDisplayHelper
{
    private static readonly Vector4 RearColor = new(0.4f, 0.8f, 1f, 1f);
    private static readonly Vector4 FlankColor = new(0.8f, 0.5f, 1f, 1f);
    private static readonly Vector4 FrontColor = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 RequiredColor = new(1f, 0.75f, 0.35f, 1f);
    private static readonly Vector4 CorrectColor = new(0.5f, 1f, 0.5f, 1f);

    public static void DrawMainWindow(in PositionalSnapshot pos)
    {
        if (!pos.HasTarget)
            return;

        ImGui.Separator();

        if (pos.TargetHasImmunity)
        {
            ImGui.Text(Loc.T(LocalizedStrings.Main.Positional, "Position:"));
            ImGui.SameLine();
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Main.PositionalImmune, "Immune"));
            return;
        }

        if (pos.RequiredPositional is { } required && !IsAtRequired(in pos, required))
        {
            ImGui.Text(Loc.T(LocalizedStrings.Main.PositionalRequired, "Required:"));
            ImGui.SameLine();
            DrawPositionalName(required, RequiredColor);
        }

        ImGui.Text(Loc.T(LocalizedStrings.Main.Positional, "Position:"));
        ImGui.SameLine();
        DrawCurrent(in pos, pos.RequiredPositional is { } req && IsAtRequired(in pos, req));
    }

    public static void DrawOverlay(in PositionalSnapshot pos)
    {
        if (!pos.HasTarget)
            return;

        ImGui.Text(Loc.T(LocalizedStrings.Overlay.PositionalLabel, "Pos:"));
        ImGui.SameLine();

        if (pos.TargetHasImmunity)
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Overlay.Immune, "Immune"));
            return;
        }

        if (pos.RequiredPositional is { } required && !IsAtRequired(in pos, required))
        {
            DrawPositionalName(required, RequiredColor);
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
        }

        DrawCurrent(in pos, pos.RequiredPositional is { } req && IsAtRequired(in pos, req));
    }

    public static void DrawDebugRequiredValue(PositionalType? required, in PositionalSnapshot pos)
    {
        if (pos.TargetHasImmunity)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Loc.T(LocalizedStrings.Debug.ImmuneOmni, "Immune (omni)"));
            return;
        }

        if (required is null)
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.NoneLabel, "None"));
            return;
        }

        var color = IsAtRequired(in pos, required.Value) ? CorrectColor : RequiredColor;
        DrawPositionalName(required.Value, color);
    }

    public static void DrawDebugCurrent(in PositionalSnapshot pos)
    {
        if (pos.TargetHasImmunity)
        {
            ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Loc.T(LocalizedStrings.Debug.ImmuneOmni, "Immune (omni)"));
            return;
        }

        if (pos.IsAtRear)
            ImGui.TextColored(CorrectColor, Loc.T(LocalizedStrings.Debug.Rear, "Rear"));
        else if (pos.IsAtFlank)
            ImGui.TextColored(new Vector4(1f, 1f, 0.5f, 1f), Loc.T(LocalizedStrings.Debug.Flank, "Flank"));
        else
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T(LocalizedStrings.Debug.Front, "Front"));
    }

    private static void DrawCurrent(in PositionalSnapshot pos, bool highlightCorrect)
    {
        if (pos.IsAtRear)
            DrawPositionalName(PositionalType.Rear, highlightCorrect ? CorrectColor : RearColor);
        else if (pos.IsAtFlank)
            DrawPositionalName(PositionalType.Flank, highlightCorrect ? CorrectColor : FlankColor);
        else
            DrawPositionalName(PositionalType.Front, FrontColor);
    }

    private static void DrawPositionalName(PositionalType positional, Vector4 color)
    {
        var text = positional switch
        {
            PositionalType.Rear => Loc.T(LocalizedStrings.Main.PositionalRear, "Rear"),
            PositionalType.Flank => Loc.T(LocalizedStrings.Main.PositionalFlank, "Flank"),
            _ => Loc.T(LocalizedStrings.Main.PositionalFront, "Front"),
        };
        ImGui.TextColored(color, text);
    }

    private static bool IsAtRequired(in PositionalSnapshot pos, PositionalType required)
        => required switch
        {
            PositionalType.Rear => pos.IsAtRear,
            PositionalType.Flank => pos.IsAtFlank,
            _ => false,
        };
}
