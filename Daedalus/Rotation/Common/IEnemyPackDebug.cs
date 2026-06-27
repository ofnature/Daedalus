namespace Daedalus.Rotation.Common;

/// <summary>
/// Job debug state fields for engaged pull size and AoE hit count.
/// </summary>
public interface IEnemyPackDebug
{
    int EngagedEnemies { get; set; }
    int AoeRangeEnemies { get; set; }
}
