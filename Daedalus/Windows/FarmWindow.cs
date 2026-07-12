using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Dalamud.Bindings.ImGui;
using Daedalus.Services.Farm;
using Daedalus.Windows.Common;

namespace Daedalus.Windows;

/// <summary>
/// Farm mode setup + control. The working profile is session-only by design (see docs/farm-mode.md);
/// saved profiles come later via FarmConfig.SavedProfiles.
/// </summary>
public sealed class FarmWindow : Window
{
    private readonly FarmModeService _farm;
    private readonly IDataManager _dataManager;
    private readonly ITargetManager _targetManager;
    private readonly IClientState _clientState;
    private readonly IObjectTable _objectTable;
    private readonly Configuration _configuration;
    private readonly System.Action _saveConfiguration;
    private readonly IFarmMountHelper _mountHelper;

    private string _itemSearch = "";
    private string _lastItemSearch = "";
    private readonly List<(uint Id, string Name)> _itemResults = new();
    private int _targetCountInput = 1;

    // Unlocked-mount picker cache — enumerated once per session (unlocks mid-session are rare;
    // the refresh button covers them).
    private List<(uint Id, string Name)>? _unlockedMounts;

    private readonly GarlandDropSource _dropSource;

    public FarmWindow(
        FarmModeService farm,
        GarlandDropSource dropSource,
        IDataManager dataManager,
        ITargetManager targetManager,
        IClientState clientState,
        IObjectTable objectTable,
        Configuration configuration,
        System.Action saveConfiguration,
        IFarmMountHelper mountHelper)
        : base("Daedalus Farm")
    {
        _farm = farm;
        _dropSource = dropSource;
        _dataManager = dataManager;
        _targetManager = targetManager;
        _clientState = clientState;
        _objectTable = objectTable;
        _configuration = configuration;
        _saveConfiguration = saveConfiguration;
        _mountHelper = mountHelper;

        Size = new Vector2(360, 480);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        var profile = _farm.Profile;

        // ----- Status / control -----
        if (_farm.IsRunning)
        {
            ImGui.TextColored(DaedalusTheme.StatusGreen, "● Farming");
            ImGui.SameLine();
            ImGui.TextDisabled(_farm.StatusLine);
            // Live bag read (not the service's poll snapshot) so the display can never lag drops.
            ImGui.Text($"{profile.ItemName}: {_farm.PeekItemCount(profile.ItemId)} / {profile.TargetCount}");
            ImGui.SameLine();
            ImGui.TextDisabled($"({_farm.Kills} kills)");

            if (ImGui.Button("Stop", new Vector2(-1, 26)))
                _farm.Stop("stopped by user");

            ImGui.Separator();
        }

        ImGui.BeginDisabled(_farm.IsRunning);

        // ----- Item -----
        ImGui.TextColored(DaedalusTheme.AccentGold, "Item");
        if (profile.ItemId != 0)
        {
            ImGui.Text($"{profile.ItemName}");
            ImGui.SameLine();
            ImGui.TextDisabled($"(id {profile.ItemId}, in bag: {_farm.PeekItemCount(profile.ItemId)})");
        }
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##farmItemSearch", "search item by name (3+ letters)...", ref _itemSearch, 64);
        if (_itemSearch.Length >= 3 && _itemSearch != _lastItemSearch)
        {
            _lastItemSearch = _itemSearch;
            RebuildItemResults();
        }
        if (_itemSearch.Length >= 3 && _itemResults.Count > 0)
        {
            if (ImGui.BeginListBox("##farmItemResults", new Vector2(-1, Math.Min(_itemResults.Count, 6) * ImGui.GetTextLineHeightWithSpacing())))
            {
                foreach (var (id, name) in _itemResults)
                {
                    if (ImGui.Selectable($"{name}##item{id}", profile.ItemId == id))
                    {
                        profile.ItemId = id;
                        profile.ItemName = name;
                        _itemSearch = "";
                        _lastItemSearch = "";
                    }
                }
                ImGui.EndListBox();
            }
        }

        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("target count", ref _targetCountInput))
            profile.TargetCount = Math.Max(1, _targetCountInput);
        _targetCountInput = profile.TargetCount;

        ImGui.Separator();

        // ----- Mobs (session-only list) -----
        ImGui.TextColored(DaedalusTheme.AccentGold, "Mobs to kill");
        ImGui.SameLine();
        ImGui.TextDisabled("(list is not saved — session only)");
        if (ImGui.Button("Add current target"))
        {
            if (_targetManager.Target is IBattleNpc npc && npc.NameId != 0)
                profile.AddMob(npc.NameId, npc.Name.ToString());
        }

        // GarlandTools dropper lookup (same source Monster Loot Hunter uses — it has no IPC).
        if (profile.ItemId != 0)
        {
            ImGui.SameLine();
            var cached = _dropSource.TryGetCached(profile.ItemId);
            if (cached == null)
            {
                ImGui.BeginDisabled(_dropSource.IsBusy);
                if (ImGui.Button(_dropSource.IsBusy ? "Looking up..." : "Find droppers"))
                    _dropSource.BeginLookup(profile.ItemId);
                ImGui.EndDisabled();
                if (_dropSource.LastError != null)
                    ImGui.TextColored(DaedalusTheme.StatusRed, _dropSource.LastError);
            }
            else if (cached.Count == 0)
            {
                ImGui.TextDisabled("GarlandTools lists no mob droppers for this item.");
            }
            else
            {
                ImGui.TextDisabled($"Drops from (GarlandTools):");
                foreach (var candidate in cached)
                {
                    var meta = candidate.LevelText.Length > 0 ? $"Lv{candidate.LevelText}" : "";
                    if (candidate.ZoneName.Length > 0)
                        meta = meta.Length > 0 ? $"{meta}, {candidate.ZoneName}" : candidate.ZoneName;
                    var label = meta.Length > 0 ? $"{candidate.Name} ({meta})" : candidate.Name;

                    if (candidate.NameId != 0)
                    {
                        if (ImGui.SmallButton($"+ {label}##drop{candidate.GarlandId}"))
                        {
                            profile.AddMob(candidate.NameId, candidate.Name);
                            // Auto-locate: flag the mob's spawn on the map, and add a farm spot
                            // when it's in the current zone (resolved async from its Garland doc).
                            if (candidate.GarlandId != 0)
                            {
                                _dropSource.BeginMobLocationLookup(candidate.GarlandId);
                                _pendingLocations[candidate.GarlandId] = candidate.Name;
                            }
                        }
                    }
                    else
                    {
                        ImGui.TextDisabled($"   {label} (couldn't match to a game mob)");
                    }
                }
            }
        }

        ProcessPendingLocations(profile);
        if (_locationStatus.Length > 0)
            ImGui.TextDisabled(_locationStatus);
        for (var i = 0; i < profile.Mobs.Count; i++)
        {
            var mob = profile.Mobs[i];
            ImGui.Text($"• {mob.Name}");
            ImGui.SameLine();
            ImGui.TextDisabled($"(NameId {mob.NameId})");
            ImGui.SameLine();
            if (ImGui.SmallButton($"remove##mob{i}"))
            {
                profile.RemoveMobAt(i);
                break;
            }
        }

        ImGui.Separator();

        // ----- Spots -----
        ImGui.TextColored(DaedalusTheme.AccentGold, "Farm spots");
        ImGui.SameLine();
        ImGui.TextDisabled("(roams between them; first = anchor)");
        if (ImGui.Button("Add spot (my position)"))
        {
            var player = _objectTable.LocalPlayer;
            if (player != null)
            {
                profile.Spots.Add(player.Position);
                profile.TerritoryId = (ushort)_clientState.TerritoryType;
            }
        }
        for (var i = 0; i < profile.Spots.Count; i++)
        {
            var spot = profile.Spots[i];
            var isActive = _farm.IsRunning && i == _farm.ActiveSpotIndex;
            ImGui.Text($"{(isActive ? "▶" : "•")} spot {i + 1}: {spot.X:F0}, {spot.Y:F0}, {spot.Z:F0}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"remove##spot{i}"))
            {
                profile.Spots.RemoveAt(i);
                break;
            }
        }

        var leash = profile.LeashRadiusYalms;
        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("leash (yalms)", ref leash, 20f, 100f, "%.0f"))
            profile.LeashRadiusYalms = leash;

        ImGui.Separator();
        DrawTravelSection();

        ImGui.EndDisabled();

        // ----- Start -----
        if (!_farm.IsRunning)
        {
            ImGui.Separator();
            ImGui.BeginDisabled(!profile.IsValid);
            ImGui.PushStyleColor(ImGuiCol.Button, DaedalusTheme.AccentGold);
            ImGui.PushStyleColor(ImGuiCol.Text, DaedalusTheme.BgDeep);
            var start = ImGui.Button("Start farming", new Vector2(-1, 28));
            ImGui.PopStyleColor(2);
            ImGui.EndDisabled();
            if (start)
            {
                var error = _farm.Start();
                if (error != null)
                    ImGui.OpenPopup("farmStartError");
                _startError = error;
            }
            if (!profile.IsValid)
                ImGui.TextDisabled("Need: item + count, ≥1 mob, ≥1 spot (all set in this zone).");

            if (ImGui.BeginPopup("farmStartError"))
            {
                ImGui.TextColored(DaedalusTheme.StatusRed, _startError ?? "unknown error");
                ImGui.EndPopup();
            }
        }
    }

    private string? _startError;
    private readonly Dictionary<ulong, string> _pendingLocations = new();
    private string _locationStatus = "";

    /// <summary>
    /// v4 travel settings (persisted in FarmConfig, unlike the session-only profile): mount mode,
    /// specific-mount picker (unlocked mounts only), fly toggle, and the two travel tuning sliders.
    /// </summary>
    private void DrawTravelSection()
    {
        var farm = _configuration.Farm;
        var changed = false;

        ImGui.TextColored(DaedalusTheme.AccentGold, "Travel");
        ImGui.SameLine();
        ImGui.TextDisabled("(mount up for long legs, fly where legal)");

        var roulette = farm.MountMode == Daedalus.Config.FarmMountMode.Roulette;
        if (ImGui.RadioButton("Mount Roulette", roulette))
        {
            farm.MountMode = Daedalus.Config.FarmMountMode.Roulette;
            changed = true;
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Specific mount", !roulette))
        {
            farm.MountMode = Daedalus.Config.FarmMountMode.Specific;
            changed = true;
        }

        if (farm.MountMode == Daedalus.Config.FarmMountMode.Specific)
        {
            _unlockedMounts ??= new List<(uint, string)>(_mountHelper.GetUnlockedMounts());

            var currentName = "pick a mount...";
            foreach (var (id, name) in _unlockedMounts)
            {
                if (id == farm.SpecificMountId)
                {
                    currentName = name;
                    break;
                }
            }

            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("##farmMountPick", currentName))
            {
                foreach (var (id, name) in _unlockedMounts)
                {
                    if (ImGui.Selectable($"{name}##mount{id}", id == farm.SpecificMountId))
                    {
                        farm.SpecificMountId = id;
                        changed = true;
                    }
                }
                ImGui.EndCombo();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("refresh##mounts"))
                _unlockedMounts = null;

            if (_unlockedMounts is { Count: 0 })
                ImGui.TextDisabled("No mounts unlocked on this character — Roulette will be used.");
            else if (farm.SpecificMountId != 0 && !_mountHelper.IsMountUnlocked(farm.SpecificMountId))
                ImGui.TextColored(DaedalusTheme.StatusRed, "Selected mount not unlocked here — falls back to Roulette.");
        }

        var flyWhenPossible = farm.FlyWhenPossible;
        if (ImGui.Checkbox("Fly when possible", ref flyWhenPossible))
        {
            farm.FlyWhenPossible = flyWhenPossible;
            changed = true;
        }

        var mountThreshold = farm.MountDistanceThresholdYalms;
        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("mount when farther than (yalms)", ref mountThreshold, 20f, 120f, "%.0f"))
        {
            farm.MountDistanceThresholdYalms = mountThreshold;
            changed = true;
        }

        var scanRadius = farm.ScanRadiusYalms;
        ImGui.SetNextItemWidth(180);
        if (ImGui.SliderFloat("mob scan radius (yalms)", ref scanRadius, 10f, 100f, "%.0f"))
        {
            farm.ScanRadiusYalms = scanRadius;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How far around the toon to look for profile mobs.\nMobs must still be inside the spot leash.");

        if (changed)
            _saveConfiguration();
    }

    /// <summary>
    /// Completes async mob-location lookups on the draw thread: sets the in-game map flag at the
    /// mob's spawn (any zone) and adds a farm spot when the mob lives in the current zone.
    /// The spot's height is estimated from the player (vNav snaps to the floor when pathing).
    /// </summary>
    private void ProcessPendingLocations(Daedalus.Services.Farm.FarmProfile profile)
    {
        if (_pendingLocations.Count == 0)
            return;

        ulong? completed = null;
        foreach (var (garlandId, mobName) in _pendingLocations)
        {
            if (!_dropSource.TryGetMobLocation(garlandId, out var location))
                continue;
            completed = garlandId;

            if (location == null)
            {
                _locationStatus = $"{mobName}: no location data on GarlandTools.";
                break;
            }

            var zone = Daedalus.Services.Farm.FarmLocationHelper.ResolveZoneByName(_dataManager, location.ZoneName);
            if (zone == null)
            {
                _locationStatus = $"{mobName}: couldn't resolve zone \"{location.ZoneName}\".";
                break;
            }

            var flagged = Daedalus.Services.Farm.FarmLocationHelper.SetMapFlag(zone.Value, location.MapX, location.MapY);

            if (zone.Value.TerritoryId == _clientState.TerritoryType)
            {
                var player = _objectTable.LocalPlayer;
                if (player != null)
                {
                    var worldX = Daedalus.Services.Farm.FarmLocationHelper.MapCoordToWorld(location.MapX, zone.Value.SizeFactor, zone.Value.OffsetX);
                    var worldZ = Daedalus.Services.Farm.FarmLocationHelper.MapCoordToWorld(location.MapY, zone.Value.SizeFactor, zone.Value.OffsetY);
                    profile.Spots.Add(new Vector3(worldX, player.Position.Y, worldZ));
                    profile.TerritoryId = (ushort)_clientState.TerritoryType;
                    _locationStatus = $"{mobName}: spot added at ({location.MapX:F1}, {location.MapY:F1}){(flagged ? " + map flag set" : "")}.";
                }
            }
            else
            {
                _locationStatus = flagged
                    ? $"{mobName} lives in {zone.Value.Name} — map flag set (farm spots must be in your current zone)."
                    : $"{mobName} lives in {zone.Value.Name} ({location.MapX:F1}, {location.MapY:F1}).";
            }
            break;
        }

        if (completed.HasValue)
            _pendingLocations.Remove(completed.Value);
    }

    private void RebuildItemResults()
    {
        _itemResults.Clear();
        var sheet = _dataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (sheet == null)
            return;

        foreach (var row in sheet)
        {
            var name = row.Name.ExtractText();
            if (name.Length == 0)
                continue;
            if (!name.Contains(_itemSearch, StringComparison.OrdinalIgnoreCase))
                continue;

            _itemResults.Add((row.RowId, name));
            if (_itemResults.Count >= 25)
                break;
        }
    }
}
