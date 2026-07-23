using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Daedalus.Models.Gear;
using Daedalus.Services.Gear;

namespace Daedalus.Windows;

/// <summary>
/// The Meld Optimizer UI (plan v2 layout: priority banner → full-width paperdoll → breakpoints +
/// optimizer row → aggregate table), extracted from the standalone window so it can be hosted in
/// the Analytics window's Melding tab AND the standalone shell. Reads only the published
/// GearSnapshot; the sweep runs on a background Task against that immutable snapshot and its
/// results are published by reference swap.
/// </summary>
public sealed class MeldOptimizerPanel
{
    private readonly GearSnapshotService _gear;
    private readonly Func<uint, string> _jobName;
    private readonly Dalamud.Plugin.Services.ITextureProvider _textureProvider;

    private const float CanvasHeight = 460f;
    private const float BoxWidth = 172f;
    private const float BoxHeight = 34f;

    // Sweep state — written by the background task, read by the draw thread.
    private volatile IReadOnlyList<MeldSweepOptimizer.MeldPlan>? _results;
    private volatile string? _sweepError;
    private volatile bool _sweeping;
    private DateTime _resultsUtc;

    /// <summary>Last time any host drew this panel — drives the gear refresh cadence.</summary>
    public DateTime LastDrawUtc { get; private set; } = DateTime.MinValue;

    private static readonly GearSlotId[] LeftColumn =
        { GearSlotId.MainHand, GearSlotId.Head, GearSlotId.Body, GearSlotId.Hands, GearSlotId.Legs, GearSlotId.Feet };

    private static readonly GearSlotId[] RightColumn =
        { GearSlotId.OffHand, GearSlotId.Ears, GearSlotId.Neck, GearSlotId.Wrists, GearSlotId.RingR, GearSlotId.RingL };

    public MeldOptimizerPanel(
        GearSnapshotService gear,
        Func<uint, string> jobName,
        Dalamud.Plugin.Services.ITextureProvider textureProvider)
    {
        _gear = gear;
        _jobName = jobName;
        _textureProvider = textureProvider;
    }

    public void Draw()
    {
        LastDrawUtc = DateTime.UtcNow;
        var snapshot = _gear.Current;

        DrawBanner(snapshot);
        ImGui.Spacing();

        Common.DaedalusTheme.GoldHeader($"Gear — avg ilvl {AverageIlvl(snapshot)}");
        DrawPaperdoll(snapshot);

        ImGui.Spacing();
        DrawMidRow(snapshot);

        ImGui.Spacing();
        DrawAggregatePanel(snapshot);
    }

    /// <summary>Plan #1's changed pieces (overlay highlight + tooltip recommendations).</summary>
    private MeldSweepOptimizer.MeldPlan? ActivePlan => _results is { Count: > 0 } results ? results[0] : null;

    // ── banner ──────────────────────────────────────────────────────────────

    private void DrawBanner(GearSnapshot snapshot)
    {
        var job = snapshot.JobId != 0 ? _jobName(snapshot.JobId) : "—";

        // Job emblem instead of a glyph — the sword emoji isn't in the ImGui font.
        var iconId = Data.JobRegistry.GetJobIconId(snapshot.JobId);
        if (iconId != 0)
        {
            var wrap = _textureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(iconId)).GetWrapOrEmpty();
            ImGui.Image(wrap.Handle, new Vector2(22, 22));
            ImGui.SameLine();
        }

        ImGui.TextColored(Common.DaedalusTheme.AccentGold, job);

        var priority = Data.BalancePriorities.For(snapshot.JobId);
        for (var i = 0; i < priority.Order.Length; i++)
        {
            ImGui.SameLine();
            if (i > 0)
            {
                ImGui.TextColored(Common.DaedalusTheme.TextSecondary, ">");
                ImGui.SameLine();
            }

            ImGui.TextColored(Common.DaedalusTheme.AccentGold, GearStatIds.Name(priority.Order[i]));
        }

        if (Array.IndexOf(priority.Order, priority.SpeedStat) < 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary, ">");
            ImGui.SameLine();
            ImGui.TextColored(Common.DaedalusTheme.TextDisabled, GearStatIds.Name(priority.SpeedStat));
        }

        ImGui.SameLine();
        ImGui.TextColored(Common.DaedalusTheme.TextSecondary, $"   {priority.Note}");

        if (snapshot.Pieces.Count == 0)
        {
            ImGui.TextColored(Common.DaedalusTheme.StatusYellow,
                "No gear snapshot yet — open in-world (refreshes every 2s while this panel is visible).");
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

        drawList.AddRectFilled(origin, origin + size, ImGui.ColorConvertFloat4ToU32(new Vector4(0.84f, 0.83f, 0.78f, 1f)), 4f);

        var anchors = DrawSilhouette(drawList, origin, size, snapshot.GenderId == 1);

        var pieces = new Dictionary<GearSlotId, GearPiece>();
        foreach (var piece in snapshot.Pieces)
            pieces[piece.Slot] = piece;

        // Overlay: slots plan #1 wants changed.
        var changedSlots = new HashSet<GearSlotId>();
        if (ActivePlan is { } plan)
        {
            foreach (var change in plan.Changes)
                changedSlots.Add(change.Slot);
        }

        DrawColumn(drawList, origin, size, LeftColumn, pieces, anchors, changedSlots, left: true);
        DrawColumn(drawList, origin, size, RightColumn, pieces, anchors, changedSlots, left: false);

        // The slot InvisibleButtons moved the cursor to the canvas bottom — rewind to the canvas
        // origin before reserving, or Dummy() stacks a SECOND canvas height of dead space below.
        ImGui.SetCursorScreenPos(origin);
        ImGui.Dummy(size);
    }

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
        HashSet<GearSlotId> changedSlots,
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
            var suboptimal = piece != null && changedSlots.Contains(slot);

            if (anchors.TryGetValue(slot, out var anchor))
            {
                var lineEnd = new Vector2(left ? boxMax.X : boxMin.X, (boxMin.Y + boxMax.Y) / 2f);
                drawList.AddLine(anchor, lineEnd, lineColor, 1.4f);
                drawList.AddCircleFilled(anchor, 4.5f, dotColor);
                drawList.AddCircle(anchor, 4.5f, ink, 0, 1.5f);
            }

            ImGui.SetCursorScreenPos(boxMin);
            ImGui.InvisibleButton($"##meldslot_{slot}", new Vector2(BoxWidth, BoxHeight));
            var hovered = ImGui.IsItemHovered();

            drawList.AddRectFilled(boxMin, boxMax, boxBg, 3f);
            var borderColor = hovered ? Common.DaedalusTheme.AccentGold
                : suboptimal ? Common.DaedalusTheme.AccentGold
                : Common.DaedalusTheme.StatusGrey;
            drawList.AddRect(boxMin, boxMax, ImGui.ColorConvertFloat4ToU32(borderColor),
                3f, ImDrawFlags.None, hovered || suboptimal ? 1.6f : 1f);

            var label = piece != null ? Truncate($"{SlotLabel(slot)}: {piece.Name}", 26) : $"{SlotLabel(slot)}: —";
            var sub = piece != null ? $"ilvl {piece.Ilvl}" : "";
            drawList.AddText(boxMin + new Vector2(6f, 3f), ImGui.ColorConvertFloat4ToU32(Common.DaedalusTheme.TextPrimary), label);
            if (sub.Length > 0)
                drawList.AddText(boxMin + new Vector2(6f, 18f), ImGui.ColorConvertFloat4ToU32(Common.DaedalusTheme.TextSecondary), sub);
            if (suboptimal)
                drawList.AddText(boxMin + new Vector2(BoxWidth - 58f, 18f), ImGui.ColorConvertFloat4ToU32(Common.DaedalusTheme.AccentGold), "re-meld");

            if (hovered && piece != null)
                DrawPieceTooltip(piece, suboptimal ? ActivePlan : null);
        }
    }

    private static void DrawPieceTooltip(GearPiece piece, MeldSweepOptimizer.MeldPlan? plan)
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

        if (plan != null)
        {
            ImGui.Spacing();
            ImGui.TextColored(Common.DaedalusTheme.AccentDim, "RECOMMENDED (PLAN #1)");
            foreach (var change in plan.Changes)
            {
                if (change.Slot != piece.Slot)
                    continue;
                var from = change.FromStat == 0 ? "empty" : GearStatIds.Name(change.FromStat);
                ImGui.TextColored(Common.DaedalusTheme.StatusGreen, $"{from} → {GearStatIds.Name(change.ToStat)}");
            }
        }

        ImGui.EndTooltip();
    }

    // ── GCD breakpoints + optimizer row ─────────────────────────────────────

    private void DrawMidRow(GearSnapshot snapshot)
    {
        var half = (ImGui.GetContentRegionAvail().X - 8f) / 2f;

        ImGui.BeginChild("##meldbreakpoints", new Vector2(half, 190f), true);
        DrawBreakpointsPanel(snapshot);
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##meldoptimize", new Vector2(half, 190f), true);
        DrawOptimizePanel(snapshot);
        ImGui.EndChild();
    }

    private void DrawOptimizePanel(GearSnapshot snapshot)
    {
        Common.DaedalusTheme.GoldHeader("Optimize Melds");

        var canSweep = snapshot.Pieces.Count > 0 && !_sweeping;
        if (!canSweep) ImGui.BeginDisabled();
        if (ImGui.Button("Optimize Melds", new Vector2(ImGui.GetContentRegionAvail().X, 26f)))
            StartSweep(snapshot);
        if (!canSweep) ImGui.EndDisabled();

        if (_sweeping)
        {
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary,
                $"sweeping {snapshot.Pieces.Sum(p => p.SweepableSockets)} XII sockets… (XI overmelds fixed)");
            return;
        }

        if (_sweepError != null)
        {
            ImGui.TextColored(Common.DaedalusTheme.StatusRed, $"sweep failed: {_sweepError}");
            return;
        }

        if (_results is not { Count: > 0 } results)
        {
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary,
                "Sweeps every XII socket across the job's priority stats, respecting per-piece caps.");
            return;
        }

        ImGui.TextColored(Common.DaedalusTheme.TextSecondary, $"computed {_resultsUtc:HH:mm:ss} — plan #1 overlays the paperdoll");
        for (var i = 0; i < results.Count; i++)
        {
            var plan = results[i];
            var deltaColor = plan.DeltaPercent > 0.005 ? Common.DaedalusTheme.StatusGreen : Common.DaedalusTheme.TextSecondary;
            ImGui.TextColored(i == 0 ? Common.DaedalusTheme.AccentGold : Common.DaedalusTheme.TextPrimary, $"#{i + 1}");
            ImGui.SameLine();
            ImGui.TextColored(deltaColor, $"{(plan.DeltaPercent >= 0 ? "+" : "")}{plan.DeltaPercent:F2}%");
            ImGui.SameLine();
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary, Truncate(plan.Summary, 60));
            if (ImGui.IsItemHovered() && plan.Summary.Length > 60)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted(plan.Summary);
                ImGui.EndTooltip();
            }
        }
    }

    private void StartSweep(GearSnapshot snapshot)
    {
        _sweeping = true;
        _sweepError = null;
        Task.Run(() =>
        {
            try
            {
                _results = MeldSweepOptimizer.Sweep(snapshot);
                _resultsUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                _sweepError = ex.Message;
            }
            finally
            {
                _sweeping = false;
            }
        });
    }

    private static void DrawBreakpointsPanel(GearSnapshot snapshot)
    {
        var priority = Data.BalancePriorities.For(snapshot.JobId);
        var speedStat = priority.SpeedStat;
        Common.DaedalusTheme.GoldHeader($"GCD Breakpoints — {GearStatIds.Name(speedStat)}");

        if (snapshot.Pieces.Count == 0)
        {
            ImGui.TextColored(Common.DaedalusTheme.TextSecondary, "No gear data.");
            return;
        }

        var aggregate = GearStatAggregator.Aggregate(snapshot);
        // Tier tables live in character-total terms: prefer the LIVE attribute (food-inclusive),
        // fall back to naked floor + gear.
        var speed = snapshot.LiveStats?.TryGetValue(speedStat, out var live) == true && live > 0
            ? live
            : MeldDpsModel.CharacterTotal(aggregate.Totals, speedStat, snapshot.Level);

        foreach (var tier in Data.GcdBreakpoints.Window(speed, snapshot.Level))
        {
            var isCurrent = speed >= tier.SpeedFrom && speed <= tier.SpeedTo;
            var color = isCurrent ? Common.DaedalusTheme.AccentGold : Common.DaedalusTheme.TextSecondary;
            var range = tier.SpeedTo >= 5999 ? $"{tier.SpeedFrom}+" : $"{tier.SpeedFrom} – {tier.SpeedTo}";
            ImGui.TextColored(color, $"{tier.GcdSeconds:F2}   {range}{(isCurrent ? $"   ← current: {speed}" : "")}");
        }

        DrawTierVerdict(aggregate.Totals, speed, snapshot.Level, priority);
    }

    private static void DrawTierVerdict(
        IReadOnlyDictionary<uint, int> totals, int speed, int level, Data.BalancePriorities.JobPriority priority)
    {
        var points = Data.GcdBreakpoints.PointsToNextTier(speed, level);
        if (points == int.MaxValue)
            return;

        var melds = (points + 53) / 54;
        var topStat = priority.Order[0];
        if (topStat == priority.SpeedStat)
        {
            ImGui.TextColored(Common.DaedalusTheme.StatusGreen,
                $"Next tier: +{points} {GearStatIds.Name(priority.SpeedStat)} ≈ {melds} meld{(melds == 1 ? "" : "s")} — speed-first job, take it.");
            return;
        }

        var candidate = new Dictionary<uint, int>(totals);
        candidate.TryGetValue(priority.SpeedStat, out var speedNow);
        candidate[priority.SpeedStat] = Math.Max(speedNow, speed) + (melds * 54);
        candidate.TryGetValue(topStat, out var topNow);
        candidate[topStat] = topNow - (melds * 54);

        var delta = MeldDpsModel.DeltaPercent(totals, candidate, level, priority.SpeedStat);
        var worth = delta > 0;
        ImGui.TextColored(worth ? Common.DaedalusTheme.StatusGreen : Common.DaedalusTheme.StatusYellow,
            $"Next tier: +{points} {GearStatIds.Name(priority.SpeedStat)} ≈ {melds} meld{(melds == 1 ? "" : "s")} from {GearStatIds.Name(topStat)} → {(delta >= 0 ? "+" : "")}{delta:F2}% — {(worth ? "worth it" : "not worth it")}");
    }

    // ── aggregate stats ─────────────────────────────────────────────────────

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
        Common.DaedalusTheme.GoldHeader("Aggregate Stats (substats shown as character totals: naked base + gear + melds)");
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

            // Everything displays as CHARACTER totals so the table matches the in-game Character
            // window: the LIVE attribute when available (includes naked base, job/clan modifiers,
            // traits, food — none gear-derivable), else the best gear-side estimate (floor + gear
            // for substats, gear-only for mains).
            var isSubstat = Array.IndexOf(GearStatIds.MeldableSubstats, statId) >= 0;
            var displayTotal = snapshot.LiveStats?.TryGetValue(statId, out var liveValue) == true && liveValue > 0
                ? liveValue
                : isSubstat ? total + Data.StatConversions.SubstatFloor(statId, snapshot.Level) : total;

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextColored(color, StatDisplayName(statId));
            ImGui.TableNextColumn();
            ImGui.TextColored(color, displayTotal.ToString());
            ImGui.TableNextColumn();
            ImGui.TextColored(isRelevant ? new Vector4(0.72f, 0.78f, 0.85f, 1f) : Common.DaedalusTheme.TextDisabled,
                DerivedText(statId, displayTotal, snapshot.Level));
            ImGui.TableNextColumn();
            DrawStatusCell(statId, aggregate, isRelevant);
        }

        ImGui.EndTable();
    }

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
