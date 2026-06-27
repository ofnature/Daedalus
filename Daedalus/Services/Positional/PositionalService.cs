using System;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;

namespace Daedalus.Services.Positional;

/// <summary>
/// Service for determining player position relative to target.
/// Uses the same dot-product classification as RSR <c>FindEnemyPositional</c>.
/// </summary>
/// <remarks>
/// Angle between target facing and direction-to-player:
/// - Front: &lt; 45° (PI/4)
/// - Rear: &gt; 135° (3PI/4)
/// - Flank: otherwise
/// </remarks>
public sealed class PositionalService : IPositionalService
{
    private const float FrontAngleRadians = MathF.PI / 4f;
    private const float RearAngleRadians = MathF.PI * 3f / 4f;

    /// <inheritdoc />
    public PositionalType GetPositional(IBattleChara player, IBattleChara target)
        => ClassifyPositional(player, target);

    /// <inheritdoc />
    public bool IsAtRear(IBattleChara player, IBattleChara target)
        => ClassifyPositional(player, target) == PositionalType.Rear;

    /// <inheritdoc />
    public bool IsAtFlank(IBattleChara player, IBattleChara target)
        => ClassifyPositional(player, target) == PositionalType.Flank;

    /// <inheritdoc />
    public bool IsAtFront(IBattleChara player, IBattleChara target)
        => ClassifyPositional(player, target) == PositionalType.Front;

    /// <inheritdoc />
    public bool HasPositionalImmunity(IBattleChara target)
    {
        if (target is IBattleNpc npc)
            return npc.SubKind == 2;

        return false;
    }

    internal static PositionalType ClassifyPositional(IBattleChara player, IBattleChara target)
    {
        var faceVec = GetFaceVector(target);
        var dir = player.Position - target.Position;
        if (dir.LengthSquared() < 1e-6f)
            return PositionalType.Front;

        dir = Vector3.Normalize(dir);
        faceVec = Vector3.Normalize(faceVec);

        var dot = Math.Clamp(Vector3.Dot(faceVec, dir), -1f, 1f);
        var angle = MathF.Acos(dot);

        if (angle < FrontAngleRadians)
            return PositionalType.Front;
        if (angle > RearAngleRadians)
            return PositionalType.Rear;
        return PositionalType.Flank;
    }

    internal static Vector3 GetFaceVector(IBattleChara battleChara)
    {
        var rotation = battleChara.Rotation;
        return new Vector3(MathF.Sin(rotation), 0f, MathF.Cos(rotation));
    }
}
