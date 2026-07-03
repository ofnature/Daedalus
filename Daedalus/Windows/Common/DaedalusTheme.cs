using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace Daedalus.Windows.Common;

/// <summary>
/// The Daedalus visual identity — single source of truth for every window's colors and the
/// small shared drawing idioms. Palette and layout rules: .cursor/rules/SKILL.md; approved
/// mockups: docs/ui-mockups/. Greek-pantheon tactical HUD: dark layers, gold accents, status
/// colors reserved for actual status.
/// </summary>
public static class DaedalusTheme
{
    // Background layers
    public static readonly Vector4 BgDeep  = new(0.08f, 0.08f, 0.10f, 1.00f);
    public static readonly Vector4 BgPanel = new(0.12f, 0.12f, 0.15f, 1.00f);
    public static readonly Vector4 BgRow   = new(0.15f, 0.15f, 0.18f, 0.60f);

    // Accent — gold/amber
    public static readonly Vector4 AccentGold = new(0.85f, 0.65f, 0.20f, 1.00f);
    public static readonly Vector4 AccentDim  = new(0.55f, 0.42f, 0.13f, 1.00f);
    /// <summary>10% gold wash for selected sidebar items / active rows.</summary>
    public static readonly Vector4 AccentWash = new(0.85f, 0.65f, 0.20f, 0.10f);

    // Status
    public static readonly Vector4 StatusGreen  = new(0.20f, 0.75f, 0.35f, 1.00f);
    public static readonly Vector4 StatusYellow = new(0.85f, 0.75f, 0.10f, 1.00f);
    public static readonly Vector4 StatusRed    = new(0.85f, 0.25f, 0.20f, 1.00f);
    public static readonly Vector4 StatusGrey   = new(0.45f, 0.45f, 0.50f, 1.00f);

    // Text
    public static readonly Vector4 TextPrimary   = new(0.92f, 0.90f, 0.85f, 1.00f);
    public static readonly Vector4 TextSecondary = new(0.60f, 0.58f, 0.55f, 1.00f);
    public static readonly Vector4 TextDisabled  = new(0.35f, 0.35f, 0.38f, 1.00f);

    /// <summary>
    /// Gold-accented section header. This Dalamud ImGui binding has no SeparatorText, so it is
    /// hand-drawn: gold label with a hairline continuing to the right edge.
    /// </summary>
    public static void GoldHeader(string label)
    {
        ImGui.Spacing();
        ImGui.TextColored(AccentGold, label);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var lineY = (min.Y + max.Y) / 2f;
        var lineStart = new Vector2(max.X + 8f, lineY);
        var lineEnd = new Vector2(ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X, lineY);
        if (lineEnd.X > lineStart.X)
            ImGui.GetWindowDrawList().AddLine(lineStart, lineEnd, ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.20f, 0.24f, 1f)), 1f);
        ImGui.Spacing();
    }

    /// <summary>Hover-help marker: dim "(?)" that shows a tooltip. Call after SameLine().</summary>
    public static void HelpMarker(string tooltip)
    {
        ImGui.TextDisabled("(?)");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    /// <summary>Colored status dot + optional label in the same color.</summary>
    public static void StatusDot(Vector4 color, string? label = null)
    {
        ImGui.TextColored(color, label is null ? "●" : $"● {label}");
    }

    /// <summary>
    /// Rounded status pill ("● Label"): dark fill, border tinted with the status color,
    /// text in the status color. Returns true when clicked.
    /// </summary>
    public static bool StatusChip(string label, Vector4 color, string id)
    {
        var text = $"● {label}";
        var padding = new Vector2(8f, 2f);
        var size = ImGui.CalcTextSize(text) + padding * 2f;
        var pos = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton(id, size);

        var dl = ImGui.GetWindowDrawList();
        var rounding = size.Y / 2f;
        var fill = new Vector4(0.15f, 0.15f, 0.18f, 1.00f);
        var border = new Vector4(color.X, color.Y, color.Z, ImGui.IsItemHovered() ? 0.60f : 0.27f);
        dl.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(fill), rounding);
        dl.AddRect(pos, pos + size, ImGui.ColorConvertFloat4ToU32(border), rounding);
        dl.AddText(pos + padding, ImGui.ColorConvertFloat4ToU32(color), text);

        return clicked;
    }

    /// <summary>HP percentage → status color (green &gt; 50%, yellow &gt; 25%, red below).</summary>
    public static Vector4 HpColor(float hpPercent) =>
        hpPercent > 0.5f ? StatusGreen : hpPercent > 0.25f ? StatusYellow : StatusRed;
}
