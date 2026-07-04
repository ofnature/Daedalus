using System.Collections.Generic;
using System.Numerics;

namespace Daedalus.Services.Farm;

/// <summary>One mob kind the farm should kill, identified by BNpcName row id (language-independent).</summary>
public readonly record struct FarmMob(uint NameId, string Name);

/// <summary>
/// The working farm profile. Runtime-only by design — lost on plugin reload/logout (the user wants
/// the list temp for now; <see cref="Config.SavedFarmProfile"/> exists for the future save/load UI).
/// </summary>
public sealed class FarmProfile
{
    private readonly List<FarmMob> _mobs = new();
    private readonly List<uint> _mobNameIds = new();

    public uint ItemId { get; set; }
    public string ItemName { get; set; } = "";
    public int TargetCount { get; set; } = 1;
    public ushort TerritoryId { get; set; }
    public float LeashRadiusYalms { get; set; } = 60f;
    public List<Vector3> Spots { get; } = new();

    public IReadOnlyList<FarmMob> Mobs => _mobs;
    public IReadOnlyCollection<uint> MobNameIds => _mobNameIds;

    public bool IsValid =>
        ItemId != 0 && TargetCount > 0 && _mobs.Count > 0 && Spots.Count > 0 && TerritoryId != 0;

    public void AddMob(uint nameId, string name)
    {
        if (nameId == 0 || _mobNameIds.Contains(nameId))
            return;
        _mobs.Add(new FarmMob(nameId, name));
        _mobNameIds.Add(nameId);
    }

    public void RemoveMobAt(int index)
    {
        if (index < 0 || index >= _mobs.Count)
            return;
        _mobNameIds.Remove(_mobs[index].NameId);
        _mobs.RemoveAt(index);
    }

    public void ClearMobs()
    {
        _mobs.Clear();
        _mobNameIds.Clear();
    }
}
