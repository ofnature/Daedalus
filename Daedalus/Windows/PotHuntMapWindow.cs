using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Daedalus.Services.Occult;
using Lumina.Excel.Sheets;

namespace Daedalus.Windows;

/// <summary>
/// Top-down map of a pot treasure hunt, centred on your character.
/// <para>
/// Each Magical Elixir reading is a ring segment — a cone from where you stood, bounded by the
/// band's min/max distance — and each is drawn in its own colour. The bright overlay on top is
/// the part that satisfies EVERY reading so far, which is the only region the coffer can be in.
/// Watching that region shrink as readings accumulate is the whole point: it either collapses
/// onto the find, or it doesn't and the band model is wrong.
/// </para>
/// <para>
/// Debug/diagnostic only. Reads the headless <see cref="PotTreasureHunt"/> and never acts.
/// </para>
/// </summary>
public sealed class PotHuntMapWindow : Window
{
    /// <summary>Half-width of the view in yalms; the map is square and the player sits at centre.</summary>
    private const float DefaultViewRadiusYalms = 90f;
    private const float MinViewRadiusYalms = 25f;
    private const float MaxViewRadiusYalms = 250f;

    /// <summary>Grid step for the intersection overlay. 2y reads smooth without being costly.</summary>
    private const float FeasibleStepYalms = 2f;

    /// <summary>Recompute the overlay at most this often — the grid test is the expensive part.</summary>
    private static readonly TimeSpan FeasibleRefresh = TimeSpan.FromMilliseconds(250);

    /// <summary>One colour per reading, cycled. Chosen to stay distinct over the map backdrop.</summary>
    private static readonly Vector4[] ReadingColours =
    [
        new(0.35f, 0.72f, 1.00f, 1f), // blue      — first scan
        new(1.00f, 0.62f, 0.28f, 1f), // orange    — second
        new(0.55f, 0.88f, 0.45f, 1f), // green
        new(0.86f, 0.51f, 0.94f, 1f), // violet
        new(0.98f, 0.85f, 0.35f, 1f), // yellow
        new(0.45f, 0.90f, 0.85f, 1f), // teal
    ];

    private static readonly Vector4 FeasibleColour = new(1.00f, 0.84f, 0.38f, 1f); // Daedalus gold
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);
    private static readonly Vector4 Warn = new(0.88f, 0.78f, 0.42f, 1f);

    private readonly PotTreasureHunt _hunt;
    private readonly IObjectTable _objectTable;
    private readonly IDataManager? _dataManager;
    private readonly ITextureProvider? _textureProvider;
    private readonly IClientState? _clientState;

    private float _viewRadius = DefaultViewRadiusYalms;
    private bool _showCones = true;
    private bool _showMap = true;

    // Resolved per territory: the zone map's texture path and the world->texture transform.
    private ushort _mapTerritory = ushort.MaxValue;
    private string? _mapTexturePath;
    private ushort _mapSizeFactor;
    private float _mapScale;      // texture pixels per yalm (SizeFactor / 100)
    private short _mapOffsetX;
    private short _mapOffsetY;

    private readonly List<Vector3> _feasible = [];
    private DateTime _feasibleComputedAt = DateTime.MinValue;
    private int _feasibleForReadingCount = -1;

    public PotHuntMapWindow(
        PotTreasureHunt hunt,
        IObjectTable objectTable,
        IDataManager? dataManager = null,
        ITextureProvider? textureProvider = null,
        IClientState? clientState = null)
        : base("Pot Hunt Map###DaedalusPotHuntMap")
    {
        _hunt = hunt;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _textureProvider = textureProvider;
        _clientState = clientState;

        // Roughly the footprint of the main Daedalus window, and square so the scale matches on
        // both axes — a stretched map would misrepresent the angles, which are the whole point.
        Size = new Vector2(340, 400);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(280, 320),
            MaximumSize = new Vector2(1200, 1200),
        };
    }

    public override void Draw()
    {
        var player = _objectTable.LocalPlayer;
        if (player is null)
        {
            ImGui.TextColored(Dim, "No player.");
            return;
        }

        var origin = player.Position;
        var bearings = _hunt.Bearings;

        DrawHeader(bearings.Count);

        DrawCoordinateCheck(origin);

        var canvasSize = ImGui.GetContentRegionAvail();
        var side = MathF.Max(80f, MathF.Min(canvasSize.X, canvasSize.Y));
        var topLeft = ImGui.GetCursorScreenPos();
        var centre = topLeft + new Vector2(side / 2f, side / 2f);
        var pixelsPerYalm = side / (_viewRadius * 2f);

        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(topLeft, topLeft + new Vector2(side, side), ImGui.GetColorU32(new Vector4(0.06f, 0.06f, 0.08f, 1f)));
        DrawZoneMap(draw, topLeft, side, origin);
        DrawGrid(draw, topLeft, side, centre, pixelsPerYalm);

        if (bearings.Count > 0)
        {
            if (_showCones)
            {
                for (var i = 0; i < bearings.Count; i++)
                    DrawCone(draw, bearings[i], ReadingColours[i % ReadingColours.Length], origin, centre, pixelsPerYalm);
            }

            DrawFeasibleRegion(draw, bearings, origin, centre, pixelsPerYalm);
        }

        DrawPlayer(draw, centre, player.Rotation);
        DrawEstimate(draw, bearings, origin, centre, pixelsPerYalm);

        ImGui.Dummy(new Vector2(side, side));
        DrawFooter(bearings);
    }

    private void DrawHeader(int readingCount)
    {
        if (_hunt.IsHunting)
            ImGui.TextColored(new Vector4(0.49f, 0.79f, 0.49f, 1f), $"HUNTING — {readingCount} reading(s)");
        else
            ImGui.TextColored(Dim, "Not hunting (Cache Me if You Can not up)");

        ImGui.SetNextItemWidth(120f);
        ImGui.SliderFloat("range", ref _viewRadius, MinViewRadiusYalms, MaxViewRadiusYalms, "%.0fy");
        ImGui.SameLine();
        ImGui.Checkbox("cones", ref _showCones);
        ImGui.SameLine();
        ImGui.Checkbox("map", ref _showMap);
    }

    /// <summary>
    /// Our computed map coordinates, for comparison against the numbers the game prints under
    /// its own map. If these two agree the world↔map transform is exactly right; if they differ
    /// by a constant the overlay is offset, and by a factor the scale is wrong. Cheaper than
    /// squinting at terrain.
    /// </summary>
    private void DrawCoordinateCheck(Vector3 origin)
    {
        if (_mapSizeFactor == 0)
        {
            ImGui.TextColored(Dim, "No map for this zone — overlay is on the plain grid.");
            return;
        }

        var mapX = Daedalus.Services.Farm.FarmLocationHelper.WorldToMapCoord(origin.X, _mapSizeFactor, _mapOffsetX);
        var mapY = Daedalus.Services.Farm.FarmLocationHelper.WorldToMapCoord(origin.Z, _mapSizeFactor, _mapOffsetY);
        ImGui.TextColored(Dim, $"You: X {mapX:0.0}  Y {mapY:0.0}   (compare to the game's map)");
    }

    /// <summary>
    /// The actual zone map behind the overlay — South Horn or North Horn, whichever you are in.
    /// Only the visible world square is sampled out of the 2048px map texture, so panning and
    /// zooming come free: the UV rectangle moves with the player instead of the image.
    /// </summary>
    private void DrawZoneMap(ImDrawListPtr draw, Vector2 topLeft, float side, Vector3 origin)
    {
        if (!_showMap || _textureProvider is null)
            return;

        ResolveMapForCurrentZone();
        if (_mapTexturePath is null || _mapScale <= 0f)
            return;

        var texture = _textureProvider.GetFromGame(_mapTexturePath).GetWrapOrDefault();
        if (texture is null)
            return;

        // Shared with the farm helper so the overlay can never drift from the transform the rest
        // of the plugin uses — tests pin it as the inner term of the display transform.
        const float TextureSize = 2048f;
        var minU = Daedalus.Services.Farm.FarmLocationHelper.WorldToMapPixel(origin.X - _viewRadius, _mapSizeFactor, _mapOffsetX) / TextureSize;
        var maxU = Daedalus.Services.Farm.FarmLocationHelper.WorldToMapPixel(origin.X + _viewRadius, _mapSizeFactor, _mapOffsetX) / TextureSize;
        var minV = Daedalus.Services.Farm.FarmLocationHelper.WorldToMapPixel(origin.Z - _viewRadius, _mapSizeFactor, _mapOffsetY) / TextureSize;
        var maxV = Daedalus.Services.Farm.FarmLocationHelper.WorldToMapPixel(origin.Z + _viewRadius, _mapSizeFactor, _mapOffsetY) / TextureSize;

        // Dimmed: the map is context, the cones and the surviving region are the content.
        draw.AddImage(
            texture.Handle,
            topLeft,
            topLeft + new Vector2(side, side),
            new Vector2(minU, minV),
            new Vector2(maxU, maxV),
            ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.55f)));
    }

    /// <summary>
    /// Looks up the current territory's map once per zone. Map textures live at
    /// <c>ui/map/{id}/{id-without-slash}_m.tex</c>, e.g. Map id "x6r1/00" →
    /// "ui/map/x6r1/00/x6r100_m.tex".
    /// </summary>
    private void ResolveMapForCurrentZone()
    {
        var territory = (ushort)(_clientState?.TerritoryType ?? 0);
        if (territory == _mapTerritory)
            return;

        _mapTerritory = territory;
        _mapTexturePath = null;
        _mapScale = 0f;

        if (_dataManager is null || territory == 0)
            return;

        try
        {
            var row = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>()?.GetRowOrDefault(territory);
            var map = row?.Map.ValueNullable;
            if (map is null || map.Value.SizeFactor == 0)
                return;

            var id = map.Value.Id.ExtractText();
            if (string.IsNullOrWhiteSpace(id))
                return;

            _mapTexturePath = $"ui/map/{id}/{id.Replace("/", string.Empty)}_m.tex";
            _mapSizeFactor = map.Value.SizeFactor;
            _mapScale = map.Value.SizeFactor / 100f;
            _mapOffsetX = map.Value.OffsetX;
            _mapOffsetY = map.Value.OffsetY;
        }
        catch
        {
            // A missing or renamed map row must not take the window down — the overlay still
            // works perfectly well against the plain grid.
            _mapTexturePath = null;
        }
    }

    /// <summary>Range rings and the cardinal cross, so distances on the map are readable.</summary>
    private void DrawGrid(ImDrawListPtr draw, Vector2 topLeft, float side, Vector2 centre, float pixelsPerYalm)
    {
        var line = ImGui.GetColorU32(new Vector4(0.20f, 0.20f, 0.24f, 1f));
        draw.AddLine(new Vector2(topLeft.X, centre.Y), new Vector2(topLeft.X + side, centre.Y), line);
        draw.AddLine(new Vector2(centre.X, topLeft.Y), new Vector2(centre.X, topLeft.Y + side), line);

        var ringStep = _viewRadius <= 50f ? 10f : _viewRadius <= 120f ? 25f : 50f;
        for (var r = ringStep; r <= _viewRadius; r += ringStep)
        {
            draw.AddCircle(centre, r * pixelsPerYalm, line, 64);
            draw.AddText(
                new Vector2(centre.X + 2f, centre.Y - (r * pixelsPerYalm) - 12f),
                ImGui.GetColorU32(Dim), $"{r:0}y");
        }

        draw.AddText(new Vector2(centre.X + 3f, topLeft.Y + 2f), ImGui.GetColorU32(Dim), "N");
    }

    /// <summary>
    /// One reading as a filled ring segment: the band's min..max radius swept across the arc.
    /// Drawn as a triangle strip between the inner and outer edge so it reads as a solid wedge.
    /// </summary>
    private static void DrawCone(
        ImDrawListPtr draw, ElixirBearing bearing, Vector4 colour,
        Vector3 playerOrigin, Vector2 centre, float pixelsPerYalm)
    {
        var (min, max) = PotTreasureTriangulation.BandRange(bearing.Proximity);
        if (max <= min)
            return;

        // The reading was taken where the player STOOD, which may not be where they are now.
        var readingCentre = centre + WorldToScreen(bearing.Origin, playerOrigin, pixelsPerYalm);

        var fill = ImGui.GetColorU32(colour with { W = 0.16f });
        var edge = ImGui.GetColorU32(colour with { W = 0.85f });

        const int segments = 28;
        var start = bearing.HeadingRadians - bearing.HalfAngleRadians;
        var sweep = bearing.HalfAngleRadians * 2f;

        Vector2 Point(float angle, float radius)
        {
            // Headings are the game's: 0 = SOUTH, ±π = north. Go through the triangulation
            // service rather than re-deriving the trig here — this used to be its own
            // (sin, -cos) under a comment claiming "0 = north", which mirrored every cone
            // north-to-south and made them disagree with the feasible region drawn beside them.
            // World Z maps straight to screen Y (south is down), the same as WorldToScreen.
            var dir = PotTreasureTriangulation.HeadingToWorldOffset(angle);
            return readingCentre + (dir * radius * pixelsPerYalm);
        }

        for (var i = 0; i < segments; i++)
        {
            var a0 = start + (sweep * i / segments);
            var a1 = start + (sweep * (i + 1) / segments);
            draw.AddQuadFilled(Point(a0, min), Point(a1, min), Point(a1, max), Point(a0, max), fill);
        }

        // Outline the two straight edges plus the outer arc — enough to read the shape.
        draw.AddLine(Point(start, min), Point(start, max), edge);
        draw.AddLine(Point(start + sweep, min), Point(start + sweep, max), edge);
        for (var i = 0; i < segments; i++)
        {
            var a0 = start + (sweep * i / segments);
            var a1 = start + (sweep * (i + 1) / segments);
            draw.AddLine(Point(a0, max), Point(a1, max), edge);
        }

        draw.AddCircleFilled(readingCentre, 3f, edge);
    }

    /// <summary>
    /// The surviving region: every grid point that satisfies ALL readings. This is the "cut out
    /// what doesn't fit" step — a point inside the first cone but outside the second simply is
    /// not drawn, so the gold area only ever shrinks as readings come in.
    /// </summary>
    private void DrawFeasibleRegion(
        ImDrawListPtr draw, IReadOnlyList<ElixirBearing> bearings,
        Vector3 playerOrigin, Vector2 centre, float pixelsPerYalm)
    {
        RefreshFeasible(bearings, playerOrigin);

        if (_feasible.Count == 0)
        {
            return;
        }

        var colour = ImGui.GetColorU32(FeasibleColour with { W = 0.40f });
        var half = FeasibleStepYalms * pixelsPerYalm * 0.5f;

        foreach (var point in _feasible)
        {
            var screen = centre + WorldToScreen(point, playerOrigin, pixelsPerYalm);
            draw.AddRectFilled(screen - new Vector2(half, half), screen + new Vector2(half, half), colour);
        }
    }

    /// <summary>
    /// Grid test, cached. Recomputed when a reading arrives, when the view changes, or on a slow
    /// timer — testing every cell every frame is wasted work when nothing has moved.
    /// </summary>
    private void RefreshFeasible(IReadOnlyList<ElixirBearing> bearings, Vector3 playerOrigin)
    {
        var now = DateTime.UtcNow;
        if (_feasibleForReadingCount == bearings.Count && now - _feasibleComputedAt < FeasibleRefresh)
            return;

        _feasibleForReadingCount = bearings.Count;
        _feasibleComputedAt = now;
        _feasible.Clear();

        for (var x = -_viewRadius; x <= _viewRadius; x += FeasibleStepYalms)
        {
            for (var z = -_viewRadius; z <= _viewRadius; z += FeasibleStepYalms)
            {
                var point = new Vector3(playerOrigin.X + x, playerOrigin.Y, playerOrigin.Z + z);
                if (PotTreasureTriangulation.SatisfiesAll(point, bearings))
                    _feasible.Add(point);
            }
        }
    }

    /// <summary>
    /// The player, with a facing arrow. Without it the map is orientation-free and a reading of
    /// "to the northeast" has to be translated in your head before you can walk it.
    /// <para>
    /// The arrow goes through the SAME heading conversion the cones use, deliberately. Drawing it
    /// with its own trig is how the cones ended up mirrored north-to-south while the region beside
    /// them was right; one conversion means the arrow and the wedges cannot disagree about which
    /// way north is.
    /// </para>
    /// </summary>
    private static void DrawPlayer(ImDrawListPtr draw, Vector2 centre, float rotation)
    {
        // Game rotation is a heading in the same convention as the readings (0 = south), and the
        // returned vector is (worldX, worldZ) — which is (screenX, screenY), since the map is
        // north-up and world Z grows southward down the screen.
        var forward = PotTreasureTriangulation.HeadingToWorldOffset(rotation);
        var dir = new Vector2(forward.X, forward.Y);
        var right = new Vector2(-dir.Y, dir.X);

        const float TipYalmsPx = 20f;
        const float BaseOffsetPx = 5f;
        const float HalfWidthPx = 6.5f;

        var tip = centre + (dir * TipYalmsPx);
        var left = centre + (dir * BaseOffsetPx) - (right * HalfWidthPx);
        var rightPt = centre + (dir * BaseOffsetPx) + (right * HalfWidthPx);

        var fill = ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 0.9f));
        draw.AddTriangleFilled(tip, left, rightPt, fill);

        // Outline in the window background so the arrow reads over the pale zone map as well as
        // over the dark grid — a white-on-white arrow is worse than no arrow.
        draw.AddTriangle(tip, left, rightPt, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.65f)), 1.5f);

        draw.AddCircleFilled(centre, 4f, ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f)));
        draw.AddCircle(centre, 7f, ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.55f)), 24);
    }

    private void DrawEstimate(
        ImDrawListPtr draw, IReadOnlyList<ElixirBearing> bearings,
        Vector3 playerOrigin, Vector2 centre, float pixelsPerYalm)
    {
        if (bearings.Count == 0 || _feasible.Count == 0)
            return;

        var sum = Vector3.Zero;
        foreach (var p in _feasible)
            sum += p;
        var estimate = sum / _feasible.Count;

        var screen = centre + WorldToScreen(estimate, playerOrigin, pixelsPerYalm);
        var colour = ImGui.GetColorU32(FeasibleColour);
        draw.AddLine(screen - new Vector2(6f, 0f), screen + new Vector2(6f, 0f), colour, 2f);
        draw.AddLine(screen - new Vector2(0f, 6f), screen + new Vector2(0f, 6f), colour, 2f);
    }

    private void DrawFooter(IReadOnlyList<ElixirBearing> bearings)
    {
        if (bearings.Count == 0)
        {
            ImGui.TextColored(Dim, "Drink a Magical Elixir to take the first reading.");
            return;
        }

        if (_feasible.Count == 0)
        {
            ImGui.TextColored(Warn, "No region satisfies every reading — a band edge or arc width is wrong.");
        }
        else
        {
            // Area is the honest progress measure: it is what actually shrinks per reading.
            var area = _feasible.Count * FeasibleStepYalms * FeasibleStepYalms;
            ImGui.TextColored(Dim, $"Search area ≈ {area:N0} sq y");
        }

        if (bearings.Count >= 2)
        {
            var quality = PotTreasureTriangulation.CrossingQuality(bearings[^2], bearings[^1]);
            if (quality < 0.35f)
                ImGui.TextColored(Warn, $"Weak crossing ({quality:P0}) — walk to one side before reading again.");
        }
        else
        {
            ImGui.TextColored(Dim, "One reading is a wedge. Move and read again to cross it.");
        }

        if (_hunt.LastFindContradictedReadings && _hunt.LastFoundAt is not null)
            ImGui.TextColored(Warn, "Last find fell OUTSIDE its readings — model needs recalibrating.");
    }

    /// <summary>World offset to screen offset. North (-Z) is up, so Z maps to +Y inverted.</summary>
    private static Vector2 WorldToScreen(Vector3 world, Vector3 origin, float pixelsPerYalm)
        => new((world.X - origin.X) * pixelsPerYalm, (world.Z - origin.Z) * pixelsPerYalm);
}
