using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Daedalus.Models.Gear;
using Daedalus.Services.Gear;

namespace Daedalus.Windows;

/// <summary>
/// Meld Optimizer window, phase 2: banner + full-width paperdoll (DrawList silhouette, anchored
/// slot boxes, hover tooltips). Layout per docs/meld-optimizer-plan.md v2 — banner on top, gear
/// pane full width scaling with the window, then (future phases) GCD breakpoints + optimizer
/// row and the aggregate table. Reads only the published GearSnapshot — never live game state.
/// </summary>
public sealed class MeldOptimizerWindow : Window
{
    private readonly GearSnapshotService _gear;
    private readonly Func<uint, string> _jobName;

    private const float CanvasHeight = 460f;
    private const float BoxWidth = 172f;
    private const float BoxHeight = 34f;

    private static readonly GearSlotId[] LeftColumn =
        { GearSlotId.MainHand, GearSlotId.Head, GearSlotId.Body, GearSlotId.Hands, GearSlotId.Legs, GearSlotId.Feet };

    private static readonly GearSlotId[] RightColumn =
        { GearSlotId.OffHand, GearSlotId.Ears, GearSlotId.Neck, GearSlotId.Wrists, GearSlotId.RingR, GearSlotId.RingL };

    public MeldOptimizerWindow(GearSnapshotService gear, Func<uint, string> jobName)
        : base("Meld Optimizer")
    {
        _gear = gear;
        _jobName = jobName;
        Size = new Vector2(980, 720);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(760, 560),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue),
        };
    }

    public override void Draw()
    {
        var snapshot = _gear.Current;

        DrawBanner(snapshot);
        ImGui.Spacing();

        Common.DaedalusTheme.GoldHeader($"Gear — avg ilvl {AverageIlvl(snapshot)}");
        DrawPaperdoll(snapshot);

        ImGui.Spacing();
        // Future row lands here per the approved layout — placeholder keeps the shape honest.
        ImGui.TextColored(Common.DaedalusTheme.TextDisabled,
            "GCD breakpoints + Optimize Melds (phase 4/5) land here.");

        ImGui.Spacing();
        DrawAggregatePanel(snapshot);
    }

    // ── aggregate stats (phase 3) ───────────────────────────────────────────

    /// <summary>Row order in the aggregate table: mains first, then substats.</summary>
    private static readonly uint[] AggregateRows =
    {
        GearStatIds.Strength, GearStatIds.Dexterity, GearStatIds.Vitality,
        GearStatIds.Intelligence, GearStatIds.Mind,
        GearStatIds.CriticalHit, GearStatIds.Determination, GearStatIds.DirectHit,
        GearStatIds.SkillSpeed, GearStatIds.SpellSpeed,
        GearStatIds.Tenacity, GearStatIds.Piety,
    };

    private static void DrawAggregatePanel(GearSnapshot snapshot)
    {
        Common.DaedalusTheme.GoldHeader("Aggregate Stats (gear + melds)");
        if (snapshot.Pieces.Count == 0)
        {
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary, "No gear data.");
            return;
        }

        var aggregate = GearStatAggregator.Aggregate(snapshot);
        var relevant = Data.GearStatRelevance.For(snapshot.JobId);

        if (!ImGui.BeginTable("##meldagg", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            return;

        ImGui.TableSetupColumn("Stat", ImGuiTableColumnFlags.WidthFixed, 130f);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 70f);
        ImGui.TableSetupColumn("Derived", ImGuiTableColumnFlags.WidthFixed, 230f);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var statId in AggregateRows)
        {
            if (!aggregate.Totals.TryGetValue(statId, out var total) || total == 0)
                continue;

            var isRelevant = relevant.Contains(statId);
            var color = isRelevant ? Common.DaedalusTheme.TextPrimary : Common.DaedalusTheme.TextDisabled;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(color, StatDisplayName(statId));
            ImGui.TableNextColumn();
            ImGui.TextColored(color, total.ToString());
            ImGui.TableNextColumn();
            ImGui.TextColored(isRelevant ? new System.Numerics.Vector4(0.72f, 0.78f, 0.85f, 1f) : Common.DaedalusTheme.TextDisabled,
                DerivedText(statId, total, snapshot.Level));
            ImGui.TableNextColumn();
            DrawStatusCell(statId, aggregate, isRelevant);
        }

        ImGui.EndTable();
    }

    /// <summary>Derived column via the shared StatConversions (characterstatus-refined port).</summary>
    private static string DerivedText(uint statId, int total, int level) => statId switch
    {
        GearStatIds.CriticalHit =>
            $"chance {Data.StatConversions.CritChancePercent(total, level):F1}% · dmg {Data.StatConversions.CritDamagePercent(total, level):F1}%",
        GearStatIds.Determination =>
            $"+{Data.StatConversions.DeterminationBonusPercent(total, level):F1}% dmg",
        GearStatIds.DirectHit =>
            $"rate {Data.StatConversions.DirectHitRatePercent(total, level):F1}%",
        GearStatIds.SkillSpeed or GearStatIds.SpellSpeed =>
            $"GCD {Data.StatConversions.GcdSeconds(total, level):F2} · +{Data.StatConversions.SpeedBonusPercent(total, level):F1}%",
        GearStatIds.Tenacity =>
            $"+{Data.StatConversions.TenacityBonusPercent(total, level):F1}% dmg/mit",
        GearStatIds.Piety =>
            $"{Data.StatConversions.PietyMpPerTick(total, level)} MP/tick",
        _ => "—",
    };

    private static void DrawStatusCell(uint statId, GearStatAggregator.AggregateResult aggregate, bool isRelevant)
    {
        // Overcap anywhere beats everything else — name the pieces so the fix is obvious.
        string? overcapText = null;
        foreach (var overcap in aggregate.Overcaps)
        {
            if (overcap.StatId != statId)
                continue;
            overcapText = overcapText == null
                ? $"overcap: {SlotLabel(overcap.Slot)} +{overcap.WastedPoints}"
                : overcapText + $", {SlotLabel(overcap.Slot)} +{overcap.WastedPoints}";
        }

        if (overcapText != null)
            ImGui.TextColored(Common.DaedalusTheme.StatusRed, overcapText + " wasted");
        else if (isRelevant)
            ImGui.TextColored(Common.DaedalusTheme.StatusGreen, "good");
        else
            ImGui.TextColored(Common.DaedalusTheme.TextDisabled, "—");
    }

    private static string StatDisplayName(uint statId) => statId switch
    {
        GearStatIds.Strength => "Strength",
        GearStatIds.Dexterity => "Dexterity",
        GearStatIds.Vitality => "Vitality",
        GearStatIds.Intelligence => "Intelligence",
        GearStatIds.Mind => "Mind",
        GearStatIds.CriticalHit => "Critical Hit",
        GearStatIds.Determination => "Determination",
        GearStatIds.DirectHit => "Direct Hit",
        GearStatIds.SkillSpeed => "Skill Speed",
        GearStatIds.SpellSpeed => "Spell Speed",
        GearStatIds.Tenacity => "Tenacity",
        GearStatIds.Piety => "Piety",
        _ => GearStatIds.Name(statId),
    };

    private void DrawBanner(GearSnapshot snapshot)
    {
        var job = snapshot.JobId != 0 ? _jobName(snapshot.JobId) : "—";
        ImGui.TextColored(Common.DaedalusTheme.AccentGold, $"⚔ {job}");
        ImGui.SameLine();
        ImGui.TextColored(Common.DaedalusTheme.TextSecondary,
            "  Balance priorities land here in phase 4.");
        if (snapshot.Pieces.Count == 0)
        {
            ImGui.TextColored(Common.DaedalusTheme.StatusYellow,
                "No gear snapshot yet — open in-world (refreshes every 2s while this window is open).");
        }
    }

    private static string AverageIlvl(GearSnapshot snapshot)
    {
        if (snapshot.Pieces.Count == 0)
            return "—";
        var total = 0;
        foreach (var piece in snapshot.Pieces)
            total += piece.Ilvl;
        return (total / snapshot.Pieces.Count).ToString();
    }

    // ── paperdoll ───────────────────────────────────────────────────────────

    private void DrawPaperdoll(GearSnapshot snapshot)
    {
        var drawList = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetContentRegionAvail().X, CanvasHeight);

        // Light background rect (the "paper").
        drawList.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0.84f, 0.83f, 0.78f, 1f)), 4f);

        var anchors = DrawSilhouette(drawList, origin, size, snapshot.GenderId == 1);

        var pieces = new Dictionary<GearSlotId, GearPiece>();
        foreach (var piece in snapshot.Pieces)
            pieces[piece.Slot] = piece;

        DrawColumn(drawList, origin, size, LeftColumn, pieces, anchors, left: true);
        DrawColumn(drawList, origin, size, RightColumn, pieces, anchors, left: false);

        // Reserve the canvas area in layout so following widgets land below it.
        ImGui.Dummy(size);
    }

    /// <summary>
    /// Bathroom-sign figure from DrawList primitives, centered in the canvas. Returns the anchor
    /// point per slot in screen space. Proportion sets per gender (female: narrower shoulders,
    /// flared lower body). All coordinates derive from the canvas center so the figure stays
    /// undistorted while the window (and leader lines) stretch.
    /// </summary>
    private static Dictionary<GearSlotId, Vector2> DrawSilhouette(
        ImDrawListPtr drawList, Vector2 origin, Vector2 size, bool female)
    {
        var ink = ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.09f, 0.10f, 1f));
        var cx = origin.X + (size.X / 2f);
        var top = origin.Y + 24f;
        const float headR = 30f;
        var headC = new Vector2(cx, top + headR);
        drawList.AddCircleFilled(headC, headR, ink);

        var anchors = new Dictionary<GearSlotId, Vector2>();
        if (!female)
        {
            var tY = headC.Y + 38f; const float tW = 112f, tH = 136f, aW = 24f, aH = 118f, lW = 38f, lH = 152f;
            drawList.AddRectFilled(new(cx - tW / 2, tY), new(cx + tW / 2, tY + tH), ink, 16f);
            drawList.AddRectFilled(new(cx - tW / 2 - aW - 6, tY + 6), new(cx - tW / 2 - 6, tY + 6 + aH), ink, 10f);
            drawList.AddRectFilled(new(cx + tW / 2 + 6, tY + 6), new(cx + tW / 2 + aW + 6, tY + 6 + aH), ink, 10f);
            drawList.AddRectFilled(new(cx - lW - 5, tY + tH + 4), new(cx - 5, tY + tH + 4 + lH), ink, 12f);
            drawList.AddRectFilled(new(cx + 5, tY + tH + 4), new(cx + lW + 5, tY + tH + 4 + lH), ink, 12f);

            anchors[GearSlotId.Head] = new(cx, headC.Y - headR + 6);
            anchors[GearSlotId.Ears] = new(cx + headR - 6, headC.Y);
            anchors[GearSlotId.Neck] = new(cx, tY + 6);
            anchors[GearSlotId.Body] = new(cx, tY + tH * 0.42f);
            anchors[GearSlotId.MainHand] = new(cx - tW / 2 - aW / 2 - 6, tY + aH);
            anchors[GearSlotId.Hands] = new(cx - tW / 2 - aW / 2 - 6, tY + aH - 20);
            anchors[GearSlotId.OffHand] = new(cx + tW / 2 + aW / 2 + 6, tY + aH);
            anchors[GearSlotId.Wrists] = new(cx + tW / 2 + aW / 2 + 6, tY + aH - 16);
            anchors[GearSlotId.RingL] = new(cx - tW / 2 - aW / 2 - 6, tY + aH + 8);
            anchors[GearSlotId.RingR] = new(cx + tW / 2 + aW / 2 + 6, tY + aH + 8);
            anchors[GearSlotId.Legs] = new(cx, tY + tH + lH * 0.35f);
            anchors[GearSlotId.Feet] = new(cx - lW / 2 - 5, tY + tH + lH);
        }
        else
        {
            var tY = headC.Y + 38f; const float tW = 96f, tH = 88f, skH = 98f, aW = 22f, aH = 112f, lW = 32f, lH = 116f;
            drawList.AddRectFilled(new(cx - tW / 2, tY), new(cx + tW / 2, tY + tH), ink, 14f);
            drawList.AddTriangleFilled(
                new(cx - tW / 2 - 4, tY + tH - 6), new(cx + tW / 2 + 4, tY + tH - 6), new(cx + tW / 2 + 28, tY + tH + skH), ink);
            drawList.AddTriangleFilled(
                new(cx - tW / 2 - 4, tY + tH - 6), new(cx - tW / 2 - 28, tY + tH + skH), new(cx + tW / 2 + 28, tY + tH + skH), ink);
            drawList.AddRectFilled(new(cx - tW / 2 - aW - 6, tY + 6), new(cx - tW / 2 - 6, tY + 6 + aH), ink, 10f);
            drawList.AddRectFilled(new(cx + tW / 2 + 6, tY + 6), new(cx + tW / 2 + aW + 6, tY + 6 + aH), ink, 10f);
            drawList.AddRectFilled(new(cx - lW - 4, tY + tH + skH - 4), new(cx - 4, tY + tH + skH - 4 + lH), ink, 11f);
            drawList.AddRectFilled(new(cx + 4, tY + tH + skH - 4), new(cx + lW + 4, tY + tH + skH - 4 + lH), ink, 11f);

            anchors[GearSlotId.Head] = new(cx, headC.Y - headR + 6);
            anchors[GearSlotId.Ears] = new(cx + headR - 6, headC.Y);
            anchors[GearSlotId.Neck] = new(cx, tY + 6);
            anchors[GearSlotId.Body] = new(cx, tY + tH * 0.5f);
            anchors[GearSlotId.MainHand] = new(cx - tW / 2 - aW / 2 - 6, tY + aH);
            anchors[GearSlotId.Hands] = new(cx - tW / 2 - aW / 2 - 6, tY + aH - 20);
            anchors[GearSlotId.OffHand] = new(cx + tW / 2 + aW / 2 + 6, tY + aH);
            anchors[GearSlotId.Wrists] = new(cx + tW / 2 + aW / 2 + 6, tY + aH - 16);
            anchors[GearSlotId.RingL] = new(cx - tW / 2 - aW / 2 - 6, tY + aH + 8);
            anchors[GearSlotId.RingR] = new(cx + tW / 2 + aW / 2 + 6, tY + aH + 8);
            anchors[GearSlotId.Legs] = new(cx, tY + tH + skH * 0.55f);
            anchors[GearSlotId.Feet] = new(cx - lW / 2 - 4, tY + tH + skH + lH - 6);
        }

        return anchors;
    }

    private void DrawColumn(
        ImDrawListPtr drawList,
        Vector2 origin,
        Vector2 size,
        GearSlotId[] slots,
        Dictionary<GearSlotId, GearPiece> pieces,
        Dictionary<GearSlotId, Vector2> anchors,
        bool left)
    {
        var boxX = left ? origin.X + 8f : origin.X + size.X - BoxWidth - 8f;
        var rowStride = (size.Y - 16f) / slots.Length;
        var lineColor = ImGui.ColorConvertFloat4ToU32(Common.DaedalusTheme.AccentDim);
        var dotColor = ImGui.ColorConvertFloat4ToU32(Common.DaedalusTheme.AccentGold);
        var boxBg = ImGui.ColorConvertFloat4ToU32(Common.DaedalusTheme.BgPanel);
        var ink = ImGui.ColorConvertFloat4ToU32(new Vector4(0.09f, 0.09f, 0.10f, 1f));

        for (var i = 0; i < slots.Length; i++)
        {
            var slot = slots[i];
            var boxMin = new Vector2(boxX, origin.Y + 8f + (i * rowStride));
            var boxMax = boxMin + new Vector2(BoxWidth, BoxHeight);
            pieces.TryGetValue(slot, out var piece);

            // Leader line from the anatomical anchor to the near edge of the box, then the dot.
            if (anchors.TryGetValue(slot, out var anchor))
            {
                var lineEnd = new Vector2(left ? boxMax.X : boxMin.X, (boxMin.Y + boxMax.Y) / 2f);
                drawList.AddLine(anchor, lineEnd, lineColor, 1.4f);
                drawList.AddCircleFilled(anchor, 4.5f, dotColor);
                drawList.AddCircle(anchor, 4.5f, ink, 0, 1.5f);
            }

            // Slot box + hover hit area.
            ImGui.SetCursorScreenPos(boxMin);
            ImGui.InvisibleButton($"##meldslot_{slot}", new Vector2(BoxWidth, BoxHeight));
            var hovered = ImGui.IsItemHovered();

            drawList.AddRectFilled(boxMin, boxMax, boxBg, 3f);
            drawList.AddRect(boxMin, boxMax,
                ImGui.ColorConvertFloat4ToU32(hovered ? Common.DaedalusTheme.AccentGold : Common.DaedalusTheme.StatusGrey),
                3f, ImDrawFlags.None, hovered ? 1.6f : 1f);

            var label = piece != null ? Truncate($"{SlotLabel(slot)}: {piece.Name}", 26) : $"{SlotLabel(slot)}: —";
            var sub = piece != null ? $"ilvl {piece.Ilvl}" : "";
            drawList.AddText(boxMin + new Vector2(6f, 3f), ImGui.ColorConvertFloat4ToU32(Common.DaedalusTheme.TextPrimary), label);
            if (sub.Length > 0)
                drawList.AddText(boxMin + new Vector2(6f, 18f), ImGui.ColorConvertFloat4ToU32(Common.DaedalusTheme.TextSecondary), sub);

            if (hovered && piece != null)
                DrawPieceTooltip(piece);
        }
    }

    private static void DrawPieceTooltip(GearPiece piece)
    {
        ImGui.BeginTooltip();
        ImGui.TextColored(Common.DaedalusTheme.AccentGold, piece.Name);
        ImGui.TextColored(Common.DaedalusTheme.TextSecondary, $"Item Level {piece.Ilvl}");
        ImGui.Separator();

        ImGui.TextColored(Common.DaedalusTheme.AccentDim, "BASE STATS");
        foreach (var (statId, value) in piece.BaseStats)
            ImGui.Text($"{GearStatIds.Name(statId)} {value}");

        if (piece.Melds.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(Common.DaedalusTheme.AccentDim, "MELDS");
            for (var i = 0; i < piece.Melds.Count; i++)
            {
                var meld = piece.Melds[i];
                ImGui.Text($"socket {i + 1}: +{meld.Value} {GearStatIds.Name(meld.StatId)} (G{meld.Grade})");
                if (meld.IsFixedOvermeld)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Common.DaedalusTheme.TextSecondary, "fixed XI");
                }

                var waste = piece.OvercapWaste(meld.StatId);
                if (waste > 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Common.DaedalusTheme.StatusRed, $"OVERCAP ({waste} wasted)");
                }
            }
        }

        ImGui.Spacing();
        ImGui.TextColored(Common.DaedalusTheme.AccentDim, "CAPS (THIS PIECE)");
        var caps = string.Empty;
        foreach (var (statId, cap) in piece.Caps)
            caps += $"{GearStatIds.Name(statId)} {cap}  ";
        ImGui.TextColored(Common.DaedalusTheme.TextSecondary, caps);
        ImGui.EndTooltip();
    }

    private static string SlotLabel(GearSlotId slot) => slot switch
    {
        GearSlotId.MainHand => "Weapon",
        GearSlotId.OffHand => "Offhand",
        GearSlotId.RingL => "Ring L",
        GearSlotId.RingR => "Ring R",
        _ => slot.ToString(),
    };

    private static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..(max - 1)] + "…";
}

