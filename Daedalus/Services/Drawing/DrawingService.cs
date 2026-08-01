using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Daedalus.Config;
using Pictomancy;

namespace Daedalus.Services.Drawing;

/// <summary>
/// World-space drawing with Pictomancy when available, ImGui screen projection otherwise.
/// </summary>
public sealed class DrawingService : IDisposable
{
    private enum DrawBackend
    {
        None,
        Pictomancy,
        ScreenSpace
    }

    private readonly DrawHelperConfig _config;
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;
    private PctDrawList? _drawList;
    private ImDrawListPtr _screenDrawList;
    private DrawBackend _backend;
    private bool _initialized;
    private bool _pictomancySessionDisabled;
    private bool _clipNativeUiBroken;
    private bool _loggedClipFailure;
    private bool _loggedPictomancyFailure;

    public bool IsDrawing => _backend != DrawBackend.None;

    /// <summary>True when Pictomancy is unavailable or disabled and the 2D fallback is active.</summary>
    public bool IsUsingScreenSpaceFallback => _backend == DrawBackend.ScreenSpace;

    public DrawingService(
        IDalamudPluginInterface pluginInterface,
        DrawHelperConfig config,
        IGameGui gameGui,
        IPluginLog log)
    {
        _config = config;
        _gameGui = gameGui;
        _log = log;

        try
        {
            PictoService.Initialize(pluginInterface);
            _initialized = true;
        }
        catch (Exception ex)
        {
            _log.Warning($"Pictomancy not available, Draw Helper will use 2D fallback: {ex.Message}");
            _initialized = false;
        }
    }

    public void BeginFrame()
    {
        _backend = DrawBackend.None;
        _drawList = null;

        if (_initialized && !_pictomancySessionDisabled && _config.UsePictomancy)
        {
            try
            {
                var clipNativeUi = _config.PictomancyClipNativeUI && !_clipNativeUiBroken;
                var hints = new PctDrawHints(
                    autoDraw: true,
                    maxAlpha: (byte)(_config.PictomancyMaxAlpha * 255f),
                    clipNativeUI: clipNativeUi);
                _drawList = PictoService.Draw(ImGui.GetWindowDrawList(), hints);
                _backend = DrawBackend.Pictomancy;
                return;
            }
            catch (Exception ex)
            {
                if (_config.PictomancyClipNativeUI && !_clipNativeUiBroken)
                {
                    _clipNativeUiBroken = true;
                    if (!_loggedClipFailure)
                    {
                        _log.Warning(
                            $"Pictomancy UI clipping failed ({ex.Message}); continuing without UI cutouts.");
                        _loggedClipFailure = true;
                    }

                    try
                    {
                        var hints = new PctDrawHints(
                            autoDraw: true,
                            maxAlpha: (byte)(_config.PictomancyMaxAlpha * 255f),
                            clipNativeUI: false);
                        _drawList = PictoService.Draw(ImGui.GetWindowDrawList(), hints);
                        _backend = DrawBackend.Pictomancy;
                        return;
                    }
                    catch (Exception retryEx)
                    {
                        DisablePictomancyForSession(retryEx.Message);
                    }
                }
                else
                {
                    DisablePictomancyForSession(ex.Message);
                }
            }
        }

        _screenDrawList = ImGui.GetWindowDrawList();
        _backend = DrawBackend.ScreenSpace;
    }

    public void EndFrame()
    {
        if (_drawList == null) return;

        var drawList = _drawList;
        _drawList = null;

        try
        {
            drawList.Dispose();
        }
        catch (Exception ex) when (_config.PictomancyClipNativeUI && !_clipNativeUiBroken)
        {
            _clipNativeUiBroken = true;
            if (!_loggedClipFailure)
            {
                _log.Warning(
                    $"Pictomancy UI clipping is incompatible with this game patch ({ex.Message}). UI cutouts disabled.");
                _loggedClipFailure = true;
            }
        }
        catch (Exception ex)
        {
            DisablePictomancyForSession(ex.Message);
        }
    }

    private void DisablePictomancyForSession(string reason)
    {
        _pictomancySessionDisabled = true;
        if (_loggedPictomancyFailure) return;

        _log.Warning($"Pictomancy disabled for this session ({reason}). Draw Helper will use 2D fallback.");
        _loggedPictomancyFailure = true;
    }

    public void DrawCircle(Vector3 center, float radius, uint color, float thickness = 2f)
    {
        if (_backend == DrawBackend.Pictomancy)
        {
            _drawList?.AddCircle(center, radius, color, thickness: thickness);
            return;
        }

        if (_backend == DrawBackend.ScreenSpace)
            DrawPolylineWorld(center, radius, 0f, MathF.PI * 2f, color, thickness, closed: true);
    }

    public void DrawCircleFilled(Vector3 center, float radius, uint color)
    {
        if (_backend == DrawBackend.Pictomancy)
        {
            _drawList?.AddCircleFilled(center, radius, color);
            return;
        }

        if (_backend == DrawBackend.ScreenSpace)
            DrawConvexWorldCircle(center, radius, color);
    }

    /// <summary>
    /// Draw a fan/cone shape. Angles in radians, FFXIV convention (negated for Pictomancy).
    /// </summary>
    public void DrawFan(Vector3 center, float innerRadius, float outerRadius,
        float startRads, float endRads, uint color, float thickness = 2f)
    {
        if (_backend == DrawBackend.Pictomancy)
        {
            if (_drawList == null) return;
            _drawList.AddFan(center, innerRadius, outerRadius,
                ToPictomancyAngle(startRads), ToPictomancyAngle(endRads),
                color, thickness: thickness);
            return;
        }

        if (_backend == DrawBackend.ScreenSpace)
            DrawFanScreen(center, innerRadius, outerRadius, startRads, endRads, color, thickness, filled: false);
    }

    public void DrawFanFilled(Vector3 center, float innerRadius, float outerRadius,
        float startRads, float endRads, uint color)
    {
        if (_backend == DrawBackend.Pictomancy)
        {
            if (_drawList == null) return;
            _drawList.AddFanFilled(center, innerRadius, outerRadius,
                ToPictomancyAngle(startRads), ToPictomancyAngle(endRads), color);
            return;
        }

        if (_backend == DrawBackend.ScreenSpace)
            DrawFanScreen(center, innerRadius, outerRadius, startRads, endRads, color, thickness: 2f, filled: true);
    }

    public void DrawDot(Vector3 position, float size, uint color)
    {
        if (_backend == DrawBackend.Pictomancy)
        {
            _drawList?.AddDot(position, size, color);
            return;
        }

        if (_backend == DrawBackend.ScreenSpace)
            DrawCircleFilled(position, size, color);
    }

    /// <summary>
    /// Straight line between two world points. Works on both backends.
    /// </summary>
    public void DrawLine(Vector3 from, Vector3 to, uint color, float thickness = 2f)
    {
        if (_backend == DrawBackend.Pictomancy)
        {
            if (_drawList == null) return;
            _drawList.PathLineTo(from);
            _drawList.PathLineTo(to);
            _drawList.PathStroke(color, PctStrokeFlags.None, thickness);
            return;
        }

        if (_backend != DrawBackend.ScreenSpace) return;
        if (!_gameGui.WorldToScreen(from, out var screenFrom)) return;
        if (!_gameGui.WorldToScreen(to, out var screenTo)) return;

        _screenDrawList.AddLine(screenFrom, screenTo, color, thickness);
    }

    public void PathLineTo(Vector3 point)
    {
        _drawList?.PathLineTo(point);
    }

    public void PathStroke(uint color, float thickness = 2f, bool closed = false)
    {
        _drawList?.PathStroke(color, closed ? PctStrokeFlags.Closed : PctStrokeFlags.None, thickness);
    }

    /// <summary>
    /// Draw a rectangle in world space using path lines (outline) or fan approximation (filled).
    /// </summary>
    public void DrawRect(Vector3 origin, float heading, float halfWidth, float length, uint color, bool filled = false, float thickness = 2f)
    {
        if (_backend == DrawBackend.Pictomancy)
        {
            if (_drawList == null) return;

            if (filled)
            {
                var fanHalfAngle = MathF.Atan2(halfWidth, length) + 0.01f;
                var fanRadius = MathF.Sqrt(length * length + halfWidth * halfWidth);
                _drawList.AddFanFilled(origin, 0f, fanRadius,
                    ToPictomancyAngle(heading - fanHalfAngle),
                    ToPictomancyAngle(heading + fanHalfAngle), color);
            }
            else
            {
                var sinH = MathF.Sin(heading);
                var cosH = MathF.Cos(heading);
                var fwd = new Vector3(sinH * length, 0, cosH * length);
                var right = new Vector3(cosH * halfWidth, 0, -sinH * halfWidth);

                _drawList.PathLineTo(origin - right);
                _drawList.PathLineTo(origin + right);
                _drawList.PathLineTo(origin + right + fwd);
                _drawList.PathLineTo(origin - right + fwd);
                _drawList.PathStroke(color, PctStrokeFlags.Closed, thickness);
            }

            return;
        }

        if (_backend == DrawBackend.ScreenSpace)
            DrawRectScreen(origin, heading, halfWidth, length, color, filled, thickness);
    }

    private void DrawPolylineWorld(
        Vector3 center,
        float radius,
        float startRadians,
        float endRadians,
        uint color,
        float thickness,
        bool closed)
    {
        Span<Vector3> worldPoints = stackalloc Vector3[WorldDrawProjection.DefaultCircleSegments + 1];
        WorldDrawProjection.SampleHorizontalArc(
            center, radius, startRadians, endRadians, WorldDrawProjection.DefaultCircleSegments, worldPoints, out var worldCount);

        Span<Vector2> screenPoints = stackalloc Vector2[worldCount];
        if (!TryProjectPoints(worldPoints, worldCount, screenPoints, out var screenCount) || screenCount < 2)
            return;

        _screenDrawList.AddPolyline(
            ref screenPoints[0],
            screenCount,
            color,
            closed ? ImDrawFlags.Closed : ImDrawFlags.None,
            thickness);
    }

    private void DrawConvexWorldCircle(Vector3 center, float radius, uint color)
    {
        Span<Vector3> worldPoints = stackalloc Vector3[WorldDrawProjection.DefaultCircleSegments];
        WorldDrawProjection.SampleHorizontalCircle(center, radius, WorldDrawProjection.DefaultCircleSegments, worldPoints, out var worldCount);

        Span<Vector2> screenPoints = stackalloc Vector2[worldCount];
        if (!TryProjectPoints(worldPoints, worldCount, screenPoints, out var screenCount) || screenCount < 3)
            return;

        _screenDrawList.AddConvexPolyFilled(ref screenPoints[0], screenCount, color);
    }

    private void DrawFanScreen(
        Vector3 center,
        float innerRadius,
        float outerRadius,
        float startRads,
        float endRads,
        uint color,
        float thickness,
        bool filled)
    {
        if (outerRadius <= 0f)
            return;

        const int segments = 24;
        Span<Vector3> worldPoints = stackalloc Vector3[segments + 3];
        var count = 0;

        if (filled && innerRadius <= 0.01f)
            worldPoints[count++] = center;

        WorldDrawProjection.SampleHorizontalArc(center, outerRadius, startRads, endRads, segments,
            worldPoints[count..], out var arcCount);
        count += arcCount;

        if (filled && innerRadius <= 0.01f && count >= 3)
        {
            Span<Vector2> screenPoints = stackalloc Vector2[count];
            if (!TryProjectPoints(worldPoints, count, screenPoints, out var screenCount) || screenCount < 3)
                return;

            _screenDrawList.AddConvexPolyFilled(ref screenPoints[0], screenCount, color);
            return;
        }

        DrawPolylineWorld(center, outerRadius, startRads, endRads, color, thickness, closed: false);

        if (innerRadius > 0.01f)
            DrawPolylineWorld(center, innerRadius, startRads, endRads, color, thickness, closed: false);
    }

    private void DrawRectScreen(Vector3 origin, float heading, float halfWidth, float length, uint color, bool filled, float thickness)
    {
        Span<Vector3> worldPoints = stackalloc Vector3[4];
        WorldDrawProjection.SampleHorizontalRect(origin, heading, halfWidth, length, worldPoints, out var worldCount);

        Span<Vector2> screenPoints = stackalloc Vector2[worldCount];
        if (!TryProjectPoints(worldPoints, worldCount, screenPoints, out var screenCount) || screenCount < 2)
            return;

        if (filled && screenCount >= 3)
        {
            _screenDrawList.AddConvexPolyFilled(ref screenPoints[0], screenCount, color);
            return;
        }

        _screenDrawList.AddPolyline(ref screenPoints[0], screenCount, color, ImDrawFlags.Closed, thickness);
    }

    private bool TryProjectPoints(ReadOnlySpan<Vector3> worldPoints, int worldCount, Span<Vector2> screenPoints, out int screenCount)
    {
        screenCount = 0;
        for (var i = 0; i < worldCount; i++)
        {
            if (!_gameGui.WorldToScreen(worldPoints[i], out var screen))
                continue;

            screenPoints[screenCount++] = screen;
        }

        return screenCount > 0;
    }

    /// <summary>
    /// Pictomancy uses opposite angle direction from FFXIV.
    /// </summary>
    private static float ToPictomancyAngle(float angle) => -angle;

    public void Dispose()
    {
        if (_initialized)
        {
            try { PictoService.Dispose(); }
            catch { /* ignore */ }
        }
    }
}
