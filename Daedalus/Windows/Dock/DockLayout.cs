using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;

namespace Daedalus.Windows.Dock;

/// <summary>One plugin's dock as the shared layout sees it.</summary>
/// <param name="Owner">The plugin that owns the dock ("Daedalus", "Charon").</param>
/// <param name="Edge">0 = left edge, 1 = right edge, 2 = top, 3 = bottom (<see cref="DockEdge"/>).</param>
/// <param name="Height">How long its row of tabs is along the edge this frame, in pixels.</param>
/// <param name="Order">Where it sits in the group, smallest first (top). Ties fall back to the owner's name.</param>
/// <param name="SeenMs">When it last published, on <see cref="Environment.TickCount64"/>.</param>
public readonly record struct DockSlot(string Owner, int Edge, float Height, int Order, long SeenMs);

/// <summary>
/// Where each plugin's edge dock goes when several share an edge. Pure.
/// <para>
/// Every dock used to centre itself on its edge, so two plugins' docks on the same side drew on top of each
/// other. Now the live docks on an edge are laid out as ONE group — stacked by <see cref="DockSlot.Order"/>,
/// the group centred — and each dock takes its own place in it. A dock that stops publishing (closed, no tab
/// applies, plugin unloaded) drops out after <see cref="StaleMs"/> and the rest close up.
/// </para>
/// <para>
/// The same code lives in Charon (<c>Charon/Features/Dock/DockLayout.cs</c>). The two must agree on the
/// share tag and the slot layout, or each falls back to centring alone.
/// </para>
/// </summary>
public static class DockLayout
{
    /// <summary>A dock that has not published for this long is gone.</summary>
    public const long StaleMs = 1000;

    /// <summary>Space between two plugins' docks — wider than the gap between one dock's own tabs.</summary>
    public const float GroupGap = 10f;

    /// <summary>
    /// The top of <paramref name="owner"/>'s dock. <paramref name="slots"/> must already include the owner's
    /// own slot for this frame; if it does not, the dock is centred on its own.
    /// </summary>
    public static float TopFor(
        string owner, int edge, float ownHeight, IEnumerable<DockSlot> slots, long nowMs,
        float workTop, float workHeight)
    {
        var live = slots
            .Where(s => s.Edge == edge && s.Height > 0f && nowMs - s.SeenMs <= StaleMs)
            .OrderBy(s => s.Order)
            .ThenBy(s => s.Owner, StringComparer.Ordinal)
            .ToList();

        var total = live.Sum(s => s.Height) + GroupGap * Math.Max(0, live.Count - 1);
        var top = workTop + MathF.Max(0f, (workHeight - total) / 2f);
        foreach (var slot in live)
        {
            if (slot.Owner == owner)
                return top;
            top += slot.Height + GroupGap;
        }

        return workTop + MathF.Max(0f, (workHeight - ownHeight) / 2f);
    }
}

/// <summary>
/// The shared list of docks, held in Dalamud's data share so every plugin's dock sees the same one.
/// <para>
/// Only framework types cross the boundary — a <c>ConcurrentDictionary&lt;string, long[]&gt;</c> keyed by
/// owner, each value <c>[edge, height, order, seenMs]</c> — because the data share hands every plugin the
/// SAME object and a type defined in one plugin's assembly would not match another's. If the share cannot be
/// had (a type clash with a different version), the dock simply centres on its own as it used to.
/// </para>
/// </summary>
public sealed class DockLayoutShare : IDisposable
{
    /// <summary>The data-share tag. Charon uses the same one; change both or neither.</summary>
    public const string Tag = "EdgeDock.Layout.v1";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly string _owner;
    private readonly int _order;
    private readonly ConcurrentDictionary<string, long[]>? _slots;

    public DockLayoutShare(IDalamudPluginInterface pluginInterface, string owner, int order)
    {
        _pluginInterface = pluginInterface;
        _owner = owner;
        _order = order;
        try
        {
            _slots = pluginInterface.GetOrCreateData(Tag, () => new ConcurrentDictionary<string, long[]>());
        }
        catch
        {
            _slots = null;
        }
    }

    /// <summary>
    /// Publish this frame's dock and get back where it starts along its edge: its top on the left and right
    /// edges, its left on the top and bottom ones (the same stacking, turned). <paramref name="length"/>,
    /// <paramref name="workStart"/> and <paramref name="workLength"/> are measured along the edge.
    /// </summary>
    public float Place(DockEdge edge, float length, float workStart, float workLength)
    {
        if (_slots is null)
            return workStart + MathF.Max(0f, (workLength - length) / 2f);

        var now = Environment.TickCount64;
        _slots[_owner] = [(int)edge, (long)MathF.Ceiling(length), _order, now];
        return DockLayout.TopFor(_owner, (int)edge, length, Read(_slots), now, workStart, workLength);
    }

    private static IEnumerable<DockSlot> Read(ConcurrentDictionary<string, long[]> slots)
    {
        foreach (var (owner, v) in slots)
        {
            if (v is { Length: >= 4 })
                yield return new DockSlot(owner, (int)v[0], v[1], (int)v[2], v[3]);
        }
    }

    public void Dispose()
    {
        if (_slots is null)
            return;

        _slots.TryRemove(_owner, out _);
        _pluginInterface.RelinquishData(Tag);
    }
}
