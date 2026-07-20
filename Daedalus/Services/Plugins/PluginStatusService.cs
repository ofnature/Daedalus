using System;
using System.Collections.Generic;
using Dalamud.Plugin;

namespace Daedalus.Services.Plugins;

/// <summary>
/// Install/load state of one companion plugin for the Settings → General plugin checklist.
/// </summary>
public sealed record PluginStatusEntry(
    string DisplayName,
    string Purpose,
    bool Required,
    bool Installed,
    bool Loaded,
    string Version);

/// <summary>
/// Scans Dalamud's installed-plugin list for the plugins Daedalus integrates with — the same
/// internal names the IPC services probe (<c>vnavmesh</c>, <c>BossModReborn</c>/<c>BossMod</c>,
/// the automation drivers, and the companion plugins). Snapshot is cached briefly; the settings
/// window reads it every frame.
/// </summary>
public sealed class PluginStatusService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(2);

    /// <summary>(display, purpose, required, internal names accepted — first match wins).</summary>
    private static readonly (string Display, string Purpose, bool Required, string[] InternalNames)[] KnownPlugins =
    [
        ("vnavmesh", "Navigation and movement — max-melee positioning, walk-to-target, Farm mode travel.",
            true, ["vnavmesh"]),
        ("BossMod Reborn", "Mechanic safety, dodging, positional hints, and AI movement (VBM also accepted).",
            true, ["BossModReborn", "BossMod"]),
        ("AutoDuty", "Dungeon-farming driver — Daedalus engages and fights while AutoDuty navigates.",
            false, ["AutoDuty"]),
        ("Questionable", "Quest driver — kill-quest targeting and combat handoff.",
            false, ["Questionable"]),
        ("Henchman", "Hunt-farming driver — standing kill order on flagged hunt marks.",
            false, ["Henchman"]),
        ("Charon", "Fleet companion — native party invites, group management, roster vitals relay.",
            false, ["Charon"]),
        ("Caduceus", "Mouseover healing companion for manual play.",
            false, ["Caduceus"]),
    ];

    private readonly IDalamudPluginInterface _pluginInterface;
    private IReadOnlyList<PluginStatusEntry> _cached = Array.Empty<PluginStatusEntry>();
    private DateTime _cachedAtUtc = DateTime.MinValue;

    public PluginStatusService(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
    }

    /// <summary>True when every required plugin is installed AND loaded.</summary>
    public bool AllRequiredLoaded
    {
        get
        {
            foreach (var entry in GetStatuses())
            {
                if (entry.Required && !entry.Loaded)
                    return false;
            }

            return true;
        }
    }

    public IReadOnlyList<PluginStatusEntry> GetStatuses()
    {
        var now = DateTime.UtcNow;
        if (now - _cachedAtUtc < CacheTtl)
            return _cached;

        var result = new List<PluginStatusEntry>(KnownPlugins.Length);
        foreach (var (display, purpose, required, internalNames) in KnownPlugins)
        {
            var installed = false;
            var loaded = false;
            var version = "";

            try
            {
                foreach (var plugin in _pluginInterface.InstalledPlugins)
                {
                    var match = false;
                    foreach (var name in internalNames)
                    {
                        if (plugin.InternalName.Equals(name, StringComparison.OrdinalIgnoreCase)
                            || plugin.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            match = true;
                            break;
                        }
                    }

                    if (!match)
                        continue;

                    installed = true;
                    // A plugin can be installed multiple ways (dev + repo) — prefer a loaded copy.
                    if (plugin.IsLoaded)
                    {
                        loaded = true;
                        version = plugin.Version.ToString();
                        break;
                    }

                    if (version.Length == 0)
                        version = plugin.Version.ToString();
                }
            }
            catch
            {
                // Dalamud may refuse the plugin list mid-load — report unknown as not installed
                // this pass; the cache retries in 2s.
            }

            result.Add(new PluginStatusEntry(display, purpose, required, installed, loaded, version));
        }

        _cached = result;
        _cachedAtUtc = now;
        return _cached;
    }
}
