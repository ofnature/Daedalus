using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Daedalus.Windows.Common;

namespace Daedalus.Windows.Dock;

/// <summary>
/// The edge dock: tabs pinned to one screen edge, each popping its panel out beside it while pointed at
/// (click to pin). Ported from Charon's <c>EdgeDockWindow</c>. There is one of these per edge; a tab shows on
/// the one whose edge it is set to, and dragging a tab and letting go near another edge moves it there.
/// <para>
/// A tab's panel is the BODY of the window it stands for — that window's own <c>Draw()</c>, called inside
/// the pop-out — so the dock and the window cannot drift apart.
/// </para>
/// <para>
/// On the left and right edges the label is ordinary text whose vertices are turned after it is drawn
/// (<see cref="DockMath"/>). It is drawn under a full-screen clip rect: ImGui culls glyphs against the clip
/// rect BEFORE they are turned, and the unturned text runs far outside a 34-pixel tab. On the top and bottom
/// edges the label reads normally.
/// </para>
/// </summary>
public sealed unsafe class EdgeDockWindow : Window
{
    /// <param name="Id">Stable id; also the ImGui id suffix.</param>
    /// <param name="Label">What the tab reads.</param>
    /// <param name="Applies">Whether the tab is shown on THIS dock right now (its edge included).</param>
    /// <param name="DrawPanel">The panel body — normally the target window's own <c>Draw()</c>.</param>
    /// <param name="PanelWidth">
    /// A fixed width with the height left to the content. Needed for bodies that size items off the space left
    /// in the window (full-width buttons, right-aligned text): in a window that sizes itself to its content,
    /// those chase each other. Null lets the panel size itself both ways.
    /// </param>
    public sealed record Tab(string Id, string Label, Func<bool> Applies, Action DrawPanel, float? PanelWidth = null);

    private const float TabThickness = 34f;
    private const float TabPadding = 14f;
    private const float TabGap = 4f;
    private const float PanelGap = 6f;
    private const float LabelScale = 1.25f;
    private const float DragThreshold = 6f;
    private static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(450);

    private readonly IReadOnlyList<Tab> _tabs;
    private readonly DockEdge _edge;
    private readonly DockLayoutShare? _layout;
    private readonly Action<string, DockEdge> _moveTab;
    private readonly DockHover _hover = new();
    private List<Tab> _visible = new();
    private Vector2 _panelSize = new(288f, 240f);
    private string? _dragging;

    /// <param name="edge">The edge this dock sits on.</param>
    /// <param name="layout">
    /// The layout shared with other plugins' docks, so docks on the same edge stack instead of overlapping.
    /// Null centres this dock on its own.
    /// </param>
    /// <param name="moveTab">A tab was dragged and let go nearest this edge: store it there.</param>
    /// <param name="windowName">The ImGui window name; each dock needs its own.</param>
    public EdgeDockWindow(
        IReadOnlyList<Tab> tabs, DockEdge edge, DockLayoutShare? layout, Action<string, DockEdge> moveTab,
        string windowName)
        : base(windowName)
    {
        _tabs = tabs;
        _edge = edge;
        _layout = layout;
        _moveTab = moveTab;

        Flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoMove
                | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav
                | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar;
        RespectCloseHotkey = false;
        PositionCondition = ImGuiCond.Always;
        IsOpen = false;
    }

    /// <summary>What the dock is showing, for diagnostics.</summary>
    public string Status { get; private set; } = "off";

    /// <summary>Whether any tab applies right now — the caller opens the window only then.</summary>
    public bool AnyTab()
    {
        _visible = _tabs.Where(t => t.Applies()).ToList();
        if (_visible.Count == 0)
            Status = "no tab applies here";
        return _visible.Count > 0;
    }

    private bool Horizontal => DockMath.IsHorizontal(_edge);

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);

        var viewport = ImGui.GetMainViewport();
        var work = viewport.WorkPos;
        var size = viewport.WorkSize;
        var length = _visible.Sum(t => TabLength(t.Label)) + TabGap * Math.Max(0, _visible.Count - 1);

        if (Horizontal)
        {
            var x = _layout?.Place(_edge, length, work.X, size.X) ?? work.X + (size.X - length) / 2f;
            var y = _edge == DockEdge.Top ? work.Y : work.Y + size.Y - TabThickness;
            Position = new Vector2(x, y);
        }
        else
        {
            var x = _edge == DockEdge.Right ? work.X + size.X - TabThickness : work.X;
            var y = _layout?.Place(_edge, length, work.Y, size.Y) ?? work.Y + (size.Y - length) / 2f;
            Position = new Vector2(x, y);
        }
    }

    public override void PostDraw() => ImGui.PopStyleVar();

    public override void Draw()
    {
        var drawList = ImGui.GetWindowDrawList();
        string? hovered = null;
        Tab? openTab = null;
        var openMin = Vector2.Zero;
        var openSize = Vector2.Zero;

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Horizontal ? new Vector2(TabGap, 0f) : new Vector2(0f, TabGap));
        for (var i = 0; i < _visible.Count; i++)
        {
            var tab = _visible[i];
            if (Horizontal && i > 0)
                ImGui.SameLine();

            var length = TabLength(tab.Label);
            var size = Horizontal ? new Vector2(length, TabThickness) : new Vector2(TabThickness, length);
            var min = ImGui.GetCursorScreenPos();

            // A press that moves becomes a drag; only a press that does not is a click (pin / unpin).
            var clicked = ImGui.InvisibleButton($"##dock{tab.Id}", size);
            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left, DragThreshold))
                _dragging = tab.Id;
            if (clicked && _dragging != tab.Id)
                _hover.Click(tab.Id);
            if (ImGui.IsItemHovered() && _dragging == null)
                hovered = tab.Id;

            var isOpen = _hover.Open == tab.Id && _dragging == null;
            var pinned = _hover.Pinned == tab.Id;
            DrawTab(drawList, min, size, tab.Label, isOpen || hovered == tab.Id, pinned, _edge);

            if (isOpen)
            {
                openTab = tab;
                openMin = min;
                openSize = size;
            }
        }

        ImGui.PopStyleVar();

        if (_dragging != null)
            DrawDrag();

        var panelBusy = openTab != null && DrawPanel(openTab, openMin, openSize);
        _hover.Update(hovered, panelBusy, DateTime.UtcNow, CloseDelay, _visible.Select(t => t.Id).ToList());

        Status = $"{_edge}: {_visible.Count} tab(s): {string.Join(", ", _visible.Select(t => t.Label))}"
                 + (_hover.Open is { } open ? $" · {open} out{(_hover.Pinned == open ? " (pinned)" : "")}" : "");
    }

    /// <summary>
    /// A tab being dragged: the edge it would land on lit gold, a copy of the tab under the pointer, and on
    /// release the move.
    /// </summary>
    private void DrawDrag()
    {
        var viewport = ImGui.GetMainViewport();
        var mouse = ImGui.GetMousePos();
        var target = DockMath.NearestEdge(mouse, viewport.WorkPos, viewport.WorkSize);

        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var id = _dragging!;
            _dragging = null;
            if (target != _edge)
                _moveTab(id, target);
            return;
        }

        var fg = ImGui.GetForegroundDrawList();
        var gold = ImGui.GetColorU32(DaedalusTheme.AccentGold);
        var a = viewport.WorkPos;
        var b = viewport.WorkPos + viewport.WorkSize;
        const float bar = 4f;
        switch (target)
        {
            case DockEdge.Left: fg.AddRectFilled(a, new Vector2(a.X + bar, b.Y), gold); break;
            case DockEdge.Right: fg.AddRectFilled(new Vector2(b.X - bar, a.Y), b, gold); break;
            case DockEdge.Top: fg.AddRectFilled(a, new Vector2(b.X, a.Y + bar), gold); break;
            case DockEdge.Bottom: fg.AddRectFilled(new Vector2(a.X, b.Y - bar), b, gold); break;
        }

        var label = _visible.FirstOrDefault(t => t.Id == _dragging)?.Label ?? _dragging!;
        var textSize = ImGui.CalcTextSize(label);
        var ghostMin = mouse + new Vector2(12f, 12f);
        var ghostMax = ghostMin + textSize + new Vector2(TabPadding * 2f, 12f);
        fg.AddRectFilled(ghostMin, ghostMax, ImGui.GetColorU32(new Vector4(0.16f, 0.15f, 0.12f, 0.92f)), 6f);
        fg.AddText(ghostMin + new Vector2(TabPadding, 6f), gold, label);
    }

    private static float TabLength(string label) =>
        MathF.Max(90f, ImGui.CalcTextSize(label).X * LabelScale + TabPadding * 2f);

    private static void DrawTab(ImDrawListPtr drawList, Vector2 min, Vector2 size, string label, bool lit, bool pinned, DockEdge edge)
    {
        var max = min + size;
        var fill = lit ? new Vector4(0.16f, 0.15f, 0.12f, 0.96f) : new Vector4(0.10f, 0.10f, 0.12f, 0.88f);
        // Rounded on the side facing the screen, square against the edge it is pinned to.
        var corners = edge switch
        {
            DockEdge.Right => ImDrawFlags.RoundCornersLeft,
            DockEdge.Top => ImDrawFlags.RoundCornersBottom,
            DockEdge.Bottom => ImDrawFlags.RoundCornersTop,
            _ => ImDrawFlags.RoundCornersRight,
        };
        drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), 6f, corners);

        if (pinned)
        {
            // Daedalus gold on the tab's screen-facing side marks a pinned panel.
            var (barMin, barSize) = edge switch
            {
                DockEdge.Right => (min, new Vector2(2f, size.Y)),
                DockEdge.Top => (new Vector2(min.X, max.Y - 2f), new Vector2(size.X, 2f)),
                DockEdge.Bottom => (min, new Vector2(size.X, 2f)),
                _ => (new Vector2(max.X - 2f, min.Y), new Vector2(2f, size.Y)),
            };
            drawList.AddRectFilled(barMin, barMin + barSize, ImGui.GetColorU32(DaedalusTheme.AccentGold));
        }

        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * LabelScale;
        var textSize = ImGui.CalcTextSize(label) * LabelScale;
        var color = ImGui.GetColorU32(lit ? DaedalusTheme.AccentGold : DaedalusTheme.TextSecondary);

        if (DockMath.IsHorizontal(edge))
        {
            drawList.AddText(font, fontSize, min + (size - textSize) / 2f, color, label);
            return;
        }

        // Turned, the label is textSize.Y wide and textSize.X tall — centred in the tab.
        var topLeft = min + new Vector2((size.X - textSize.Y) / 2f, (size.Y - textSize.X) / 2f);
        var pivot = DockMath.PivotFor(topLeft, textSize);

        // Drawn first near the top-left of the screen, where the whole unturned line fits, then turned and
        // moved into the tab.
        var drawnAt = ImGui.GetMainViewport().WorkPos + new Vector2(8f, 8f);
        drawList.PushClipRectFullScreen();
        var first = drawList.VtxBuffer.Size;
        drawList.AddText(font, fontSize, drawnAt, color, label);
        var last = drawList.VtxBuffer.Size;
        var vertices = drawList.VtxBuffer.Data;
        for (var i = first; i < last; i++)
            vertices[i].Pos = DockMath.TurnUpAndPlace(vertices[i].Pos, drawnAt, pivot);
        drawList.PopClipRect();
    }

    /// <summary>Draws the open tab's panel beside it. True while the pointer is on it or it is mid-interaction.</summary>
    private bool DrawPanel(Tab tab, Vector2 tabMin, Vector2 tabSize)
    {
        var viewport = ImGui.GetMainViewport();
        var (pos, pivot) = DockMath.PanelPlacement(
            _edge, tabMin, tabSize, _panelSize, viewport.WorkPos, viewport.WorkSize, PanelGap);

        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, pivot);
        if (tab.PanelWidth is { } width)
            ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, float.MaxValue));

        var flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoSavedSettings
                    | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.AlwaysAutoResize;

        var busy = false;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10f, 8f));
        if (ImGui.Begin($"{WindowName}Panel{tab.Id}", flags))
        {
            try
            {
                tab.DrawPanel();
            }
            catch (Exception ex)
            {
                // Caught HERE: an exception leaving between Begin and End would skip the End and corrupt
                // ImGui's window stack for every plugin drawing after this one.
                ImGui.TextColored(DaedalusTheme.StatusRed, $"This panel could not be drawn: {ex.Message}");
            }

            _panelSize = ImGui.GetWindowSize();

            // The panel also counts as "in use" while one of its own pop-ups is open or a control is held:
            // a combo's list is a separate window, and losing the panel under it closes the combo.
            busy = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows
                                         | ImGuiHoveredFlags.AllowWhenBlockedByPopup
                                         | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
                   || ImGui.IsPopupOpen(string.Empty, ImGuiPopupFlags.AnyPopupId | ImGuiPopupFlags.AnyPopupLevel)
                   || ImGui.IsAnyItemActive();
        }

        ImGui.End();
        ImGui.PopStyleVar();
        return busy;
    }
}
