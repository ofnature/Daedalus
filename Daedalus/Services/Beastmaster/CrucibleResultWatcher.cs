using System;
using System.Collections.Generic;
using Daedalus.Data;
using static Daedalus.Services.Beastmaster.CrucibleAddons;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// Reads a board's result screen the moment it opens (the autopilot closes it within seconds, autopilot or not) and
/// hands it on once per screen. Layout in <see cref="CrucibleRunResult"/>.
/// </summary>
public sealed unsafe class CrucibleResultWatcher
{
    /// <summary>The highest value index read ([69] = rank).</summary>
    private const int LastIndex = 69;

    private bool _captured;

    /// <summary>A result screen was read.</summary>
    public event Action<CrucibleRunResult>? ResultRead;

    /// <summary>The last result read this session, or null.</summary>
    public CrucibleRunResult? Last { get; private set; }

    /// <summary>Framework thread, every frame.</summary>
    public void Update()
    {
        var screen = Visible("XBMResult");
        if (screen == null)
        {
            _captured = false;
            return;
        }

        if (_captured || screen->AtkValuesCount <= LastIndex || Number(screen, 68) == 0)
            return; // already read, or not filled in yet

        _captured = true;
        var count = Math.Clamp((int)Number(screen, 14), 0, 16);
        var bonuses = new List<(string, int)>(count);
        for (var i = 0; i < count; i++)
            bonuses.Add((Text(screen, 15 + i), (int)Number(screen, 31 + i)));

        Last = new CrucibleRunResult(
            Text(screen, 0), Text(screen, 1), (int)Number(screen, 68), Text(screen, 69), (int)Number(screen, 2),
            Text(screen, 3), (int)Number(screen, 4),
            Text(screen, 5), (int)Number(screen, 6),
            Text(screen, 7), (int)Number(screen, 8),
            Text(screen, 9), (int)Number(screen, 10),
            (int)Number(screen, 13), bonuses,
            (int)Number(screen, 63), Text(screen, 58));
        ResultRead?.Invoke(Last);
    }
}
