namespace Daedalus.Services.Targeting;

/// <summary>
/// Engaged pull size vs enemies within a job's self-centered AoE radius.
/// </summary>
public readonly record struct EnemyPackCounts(int Engaged, int AoeRange);
