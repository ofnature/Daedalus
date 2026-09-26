using System;
using System.Collections.Generic;

namespace Daedalus.Services.Diagnostics;

/// <summary>Everything in Daedalus that can put a body back on its feet.</summary>
public enum ReviveSource
{
    /// <summary>The job's own raise — Egeiro, Ascend, Resurrection, Raise.</summary>
    HealerRaise,

    /// <summary>The Phoenix Down safety net, for when every healer is down.</summary>
    PhoenixDown,

    /// <summary>Variant Raise, from a variant dungeon's duty actions.</summary>
    VariantRaise,

    /// <summary>The Occult Crescent phantom job raise.</summary>
    PhantomRaise,

    /// <summary>Lost Arise / Lost Sacrifice, from a Bozja duty action slot.</summary>
    BozjaRaise,
}

/// <summary>
/// One place every revive path reports to, so "why is nobody rezzing" has a single answer.
///
/// <para>
/// Each path already worked out a precise reason every frame and then dropped it: the healer's went
/// into a job-specific debug tab, the variant layer's into a line nobody thought to read, and Phoenix
/// Down's went nowhere at all. Diagnosing a dead healer that stayed dead therefore meant reading four
/// subsystems' source instead of one screen — which across 2026-09-19/20 produced four wrong guesses
/// before the real cause turned up. The Revive tab reads this.
/// </para>
///
/// <para>
/// Static because the reporters are spread across the rotation stack, a framework-tick service and a
/// duty layer, with no shared owner between them. Write-only from the game's side and read only by the
/// debug UI, so nothing behavioural depends on it.
/// </para>
/// </summary>
public static class ReviveDiagnostics
{
    /// <summary>A reported state and when it was last seen.</summary>
    public readonly record struct Entry(ReviveSource Source, string State, DateTime AtUtc)
    {
        /// <summary>How long ago this was reported — a stale line is itself the finding.</summary>
        public double AgeSeconds => Math.Max(0d, (DateTime.UtcNow - AtUtc).TotalSeconds);
    }

    private static readonly object Gate = new();
    private static readonly Dictionary<ReviveSource, Entry> Latest = [];

    /// <summary>Record this path's current verdict. Cheap enough to call every frame.</summary>
    public static void Report(ReviveSource source, string state)
    {
        if (string.IsNullOrWhiteSpace(state))
            return;

        lock (Gate)
        {
            // Keep the original timestamp while the verdict is unchanged, so the age reads as "how
            // long has it been saying this" rather than resetting every frame.
            if (Latest.TryGetValue(source, out var prev) && prev.State == state)
                return;

            Latest[source] = new Entry(source, state, DateTime.UtcNow);
        }
    }

    /// <summary>Every path that has reported, newest first.</summary>
    public static IReadOnlyList<Entry> Snapshot()
    {
        lock (Gate)
        {
            var all = new List<Entry>(Latest.Values);
            all.Sort(static (a, b) => b.AtUtc.CompareTo(a.AtUtc));
            return all;
        }
    }

    /// <summary>The latest verdict from one path, or null if it has never reported.</summary>
    public static Entry? For(ReviveSource source)
    {
        lock (Gate)
            return Latest.TryGetValue(source, out var e) ? e : null;
    }

    /// <summary>Test/reload hygiene.</summary>
    public static void Reset()
    {
        lock (Gate)
            Latest.Clear();
    }
}
