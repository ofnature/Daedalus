using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

namespace Daedalus.Services.Drawing;

/// <summary>Treasure coffer tiers, resolved from the coffer's scenery model.</summary>
public enum TreasureTier
{
    Unknown,
    Bronze,
    Silver,
    Gold,
}

/// <summary>
/// Picks the world objects a guide line is drawn to. Split out from the canvas so the
/// filters are testable without ImGui.
/// <para>
/// Object ids and the tier→model mapping are taken from BOCCHI (OhKannaDuh/BOCCHI), which is
/// the field-proven source for Occult Crescent object data.
/// </para>
/// </summary>
public static class WorldLineSelector
{
    /// <summary>EventObj BaseId of an Occult Crescent carrot spot — the one a Fortune Carrot turns into a chest.</summary>
    public const uint CarrotBaseId = 2010139;

    // Other known Occult EventObj ids, kept for reference: bunny chest 2012936,
    // knowledge crystal 2007457, trap 2014584, big trap 2014585.

    /// <summary>
    /// ExportedSG row ids that identify a coffer's tier. These are consecutive rows holding
    /// consecutive coffer models — 1596 is <c>sgbg_w_tbx_001_01a</c>, 1597 <c>..._002_01a</c>,
    /// 1598 <c>..._003_01a</c>.
    /// <para>
    /// Bronze and silver are BOCCHI's, field-proven. Gold is INFERRED from that model sequence
    /// and not yet confirmed against a live gold coffer — BOCCHI stops at silver, so gold shows
    /// as Unknown there. If a gold coffer ever draws in the wrong colour, this line is why.
    /// </para>
    /// </summary>
    private const uint BronzeSceneryId = 1596;
    private const uint SilverSceneryId = 1597;
    private const uint GoldSceneryId = 1598;

    /// <summary>Object kinds shown by the world-object label diagnostic — everything that is not a creature.</summary>
    public static bool IsLabelCandidate(ObjectKind kind) =>
        kind is not (ObjectKind.None or ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.Companion
            or ObjectKind.Retainer or ObjectKind.Mount or ObjectKind.Ornament);

    /// <summary>
    /// Treasure coffers. Targetability is what drops a coffer once it has been opened, so the
    /// line disappears on loot rather than hanging over an empty chest.
    /// </summary>
    public static bool IsChestLineCandidate(IGameObject obj, Vector3 origin, float maxDistance)
    {
        if (obj.ObjectKind != ObjectKind.Treasure) return false;
        if (obj.IsDead || !obj.IsTargetable) return false;

        return WithinRange(obj, origin, maxDistance);
    }

    /// <summary>
    /// Occult Crescent carrot spots. Matched on BaseId — carrots share
    /// <see cref="ObjectKind.EventObj"/> with every door and trigger volume in the zone, and
    /// unlike coffers they are not reliably targetable, so the id is the only sound filter.
    /// </summary>
    public static bool IsCarrotLineCandidate(IGameObject obj, Vector3 origin, float maxDistance)
    {
        if (obj.ObjectKind != ObjectKind.EventObj) return false;
        if (obj.BaseId != CarrotBaseId) return false;
        if (obj.IsDead) return false;

        return WithinRange(obj, origin, maxDistance);
    }

    /// <summary>Maps a coffer's scenery model id to its tier.</summary>
    public static TreasureTier TierFromSceneryId(uint sceneryId) => sceneryId switch
    {
        BronzeSceneryId => TreasureTier.Bronze,
        SilverSceneryId => TreasureTier.Silver,
        GoldSceneryId => TreasureTier.Gold,
        _ => TreasureTier.Unknown,
    };

    private static bool WithinRange(IGameObject obj, Vector3 origin, float maxDistance)
    {
        if (maxDistance <= 0f) return false;

        return Vector3.DistanceSquared(origin, obj.Position) <= maxDistance * maxDistance;
    }
}
