using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Daedalus.Data;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Beastmaster;

/// <summary>What the Crucible tracker sees this frame.</summary>
/// <param name="Board">The board you are on; null outside the Crucible.</param>
/// <param name="Enemy">The enemy you are fighting (your target if it is one, else the nearest), or null.</param>
/// <param name="Lineup">Everyone in that enemy's battle, leader first.</param>
public sealed record CrucibleSnapshot(CrucibleBoard? Board, CrucibleEnemy? Enemy, IReadOnlyList<CrucibleEnemy> Lineup)
{
    public static readonly CrucibleSnapshot None = new(null, null, []);

    public bool OnBoard => Board != null;
}

/// <summary>
/// Which Crucible board you are on and which enemy you are fighting. Updated once a frame by the plugin; read by the
/// Crucible window and by Artemis (Snarl / Challenge).
/// <para>
/// <see cref="Current"/> is static on purpose: rotations read a COPY of the configuration, and a transient flag kept on
/// an instance they never see is how the Debug window's "is open" flag once froze every job (see the automation-bridges
/// note). The board you are standing on is the same kind of fact.
/// </para>
/// </summary>
public sealed class CrucibleTracker
{
    /// <summary>How far away an enemy can be and still count as the one you are fighting, with nothing targeted.</summary>
    public const float NearbyYalms = 40f;

    private readonly CrucibleIndex _index;

    public CrucibleTracker(CrucibleIndex index) => _index = index;

    /// <summary>This frame's snapshot. <see cref="CrucibleSnapshot.None"/> outside the Crucible.</summary>
    public static CrucibleSnapshot Current { get; private set; } = CrucibleSnapshot.None;

    public CrucibleIndex Index => _index;

    /// <summary>Framework thread, every frame.</summary>
    public void Update(uint territoryId, IPlayerCharacter? player, IGameObject? target, IObjectTable objects)
    {
        var board = _index.BoardForTerritory(territoryId);
        if (board == null || player == null)
        {
            Current = board == null ? CrucibleSnapshot.None : new CrucibleSnapshot(board, null, []);
            return;
        }

        var enemy = target is IBattleNpc targeted && !targeted.IsDead ? _index.FindEnemy(board, targeted.NameId) : null;
        if (enemy == null)
        {
            enemy = objects.OfType<IBattleNpc>()
                .Where(n => !n.IsDead && Vector3.Distance(n.Position, player.Position) <= NearbyYalms)
                .OrderBy(n => Vector3.Distance(n.Position, player.Position))
                .Select(n => _index.FindEnemy(board, n.NameId))
                .FirstOrDefault(e => e != null);
        }

        Current = new CrucibleSnapshot(board, enemy, enemy == null ? [] : _index.EnemiesInBattle(enemy.Battle));
    }
}
