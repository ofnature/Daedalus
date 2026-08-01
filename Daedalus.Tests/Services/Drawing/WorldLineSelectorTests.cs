using System.Numerics;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Moq;
using Daedalus.Services.Drawing;
using Xunit;

namespace Daedalus.Tests.Services.Drawing;

public sealed class WorldLineSelectorTests
{
    private static IGameObject Obj(
        ObjectKind kind,
        Vector3 position,
        bool targetable = true,
        bool dead = false,
        uint baseId = 0)
    {
        var obj = new Mock<IGameObject>();
        obj.Setup(x => x.ObjectKind).Returns(kind);
        obj.Setup(x => x.Position).Returns(position);
        obj.Setup(x => x.IsTargetable).Returns(targetable);
        obj.Setup(x => x.IsDead).Returns(dead);
        obj.Setup(x => x.BaseId).Returns(baseId);
        return obj.Object;
    }

    private static IGameObject Carrot(Vector3 position, bool dead = false) =>
        Obj(ObjectKind.EventObj, position, dead: dead, baseId: WorldLineSelector.CarrotBaseId);

    // ── Treasure coffers ──

    [Fact]
    public void IsChestLineCandidate_AcceptsTreasureInRange()
    {
        var chest = Obj(ObjectKind.Treasure, new Vector3(10f, 0f, 0f));

        Assert.True(WorldLineSelector.IsChestLineCandidate(chest, Vector3.Zero, 100f));
    }

    [Theory]
    [InlineData(ObjectKind.BattleNpc)]
    [InlineData(ObjectKind.EventObj)]
    [InlineData(ObjectKind.Pc)]
    [InlineData(ObjectKind.GatheringPoint)]
    public void IsChestLineCandidate_RejectsNonTreasureKinds(ObjectKind kind)
    {
        var obj = Obj(kind, new Vector3(1f, 0f, 0f));

        Assert.False(WorldLineSelector.IsChestLineCandidate(obj, Vector3.Zero, 100f));
    }

    /// <summary>
    /// Gold coffers are EventObj, not Treasure — field-confirmed. Keying chests on ObjectKind
    /// alone silently skipped the most valuable chest in the zone.
    /// </summary>
    [Fact]
    public void IsChestLineCandidate_AcceptsGoldCofferEventObject()
    {
        var gold = Obj(ObjectKind.EventObj, new Vector3(0f, 0f, 10f),
            baseId: WorldLineSelector.PotGoldCofferBaseId);

        Assert.True(WorldLineSelector.IsChestLineCandidate(gold, Vector3.Zero, 100f));
    }

    [Fact]
    public void IsChestLineCandidate_RejectsOpenedGoldCoffer()
    {
        var looted = Obj(ObjectKind.EventObj, new Vector3(0f, 0f, 10f),
            targetable: false, baseId: WorldLineSelector.PotGoldCofferBaseId);

        Assert.False(WorldLineSelector.IsChestLineCandidate(looted, Vector3.Zero, 100f));
    }

    [Fact]
    public void TierFromCofferBaseId_MapsGoldCoffer()
    {
        Assert.Equal(TreasureTier.Gold, WorldLineSelector.TierFromCofferBaseId(WorldLineSelector.PotGoldCofferBaseId));
        Assert.Equal(TreasureTier.Unknown, WorldLineSelector.TierFromCofferBaseId(WorldLineSelector.CarrotBaseId));
    }

    /// <summary>A carrot spot must not be mistaken for a coffer just because both are EventObj.</summary>
    [Fact]
    public void IsChestLineCandidate_RejectsCarrotSpot()
    {
        var carrot = Obj(ObjectKind.EventObj, new Vector3(0f, 0f, 10f),
            baseId: WorldLineSelector.CarrotBaseId);

        Assert.False(WorldLineSelector.IsChestLineCandidate(carrot, Vector3.Zero, 100f));
    }

    /// <summary>An opened coffer stops being targetable — the line should drop with it.</summary>
    [Fact]
    public void IsChestLineCandidate_RejectsOpenedCoffer()
    {
        var opened = Obj(ObjectKind.Treasure, new Vector3(10f, 0f, 0f), targetable: false);

        Assert.False(WorldLineSelector.IsChestLineCandidate(opened, Vector3.Zero, 100f));
    }

    [Fact]
    public void IsChestLineCandidate_RejectsTreasureBeyondMaxDistance()
    {
        var chest = Obj(ObjectKind.Treasure, new Vector3(0f, 0f, 120f));

        Assert.False(WorldLineSelector.IsChestLineCandidate(chest, Vector3.Zero, 100f));
    }

    /// <summary>Vertical separation counts — a coffer on the floor below should not draw through the ceiling.</summary>
    [Fact]
    public void IsChestLineCandidate_MeasuresDistanceInThreeDimensions()
    {
        var chest = Obj(ObjectKind.Treasure, new Vector3(9f, 9f, 9f));

        Assert.True(WorldLineSelector.IsChestLineCandidate(chest, Vector3.Zero, 20f));
        Assert.False(WorldLineSelector.IsChestLineCandidate(chest, Vector3.Zero, 15f));
    }

    [Fact]
    public void IsChestLineCandidate_RejectsEverythingWhenRangeIsZero()
    {
        var chest = Obj(ObjectKind.Treasure, Vector3.Zero);

        Assert.False(WorldLineSelector.IsChestLineCandidate(chest, Vector3.Zero, 0f));
    }

    // ── Occult carrot spots ──

    [Fact]
    public void IsCarrotLineCandidate_AcceptsCarrotInRange()
    {
        Assert.True(WorldLineSelector.IsCarrotLineCandidate(Carrot(new Vector3(0f, 0f, 12f)), Vector3.Zero, 100f));
    }

    /// <summary>
    /// Carrots are not reliably targetable, so the filter must not require it — this is the case
    /// an IsTargetable-based filter got wrong.
    /// </summary>
    [Fact]
    public void IsCarrotLineCandidate_AcceptsUntargetableCarrot()
    {
        var carrot = Obj(ObjectKind.EventObj, new Vector3(0f, 0f, 12f),
            targetable: false, baseId: WorldLineSelector.CarrotBaseId);

        Assert.True(WorldLineSelector.IsCarrotLineCandidate(carrot, Vector3.Zero, 100f));
    }

    /// <summary>Doors, trigger volumes and every other bit of scenery share EventObj.</summary>
    [Fact]
    public void IsCarrotLineCandidate_RejectsOtherEventObjects()
    {
        var scenery = Obj(ObjectKind.EventObj, new Vector3(0f, 0f, 12f), baseId: 2007457); // knowledge crystal

        Assert.False(WorldLineSelector.IsCarrotLineCandidate(scenery, Vector3.Zero, 100f));
    }

    [Fact]
    public void IsCarrotLineCandidate_RejectsMatchingIdOnWrongKind()
    {
        var impostor = Obj(ObjectKind.Treasure, new Vector3(0f, 0f, 12f), baseId: WorldLineSelector.CarrotBaseId);

        Assert.False(WorldLineSelector.IsCarrotLineCandidate(impostor, Vector3.Zero, 100f));
    }

    [Fact]
    public void IsCarrotLineCandidate_RejectsBeyondMaxDistance()
    {
        Assert.False(WorldLineSelector.IsCarrotLineCandidate(Carrot(new Vector3(0f, 0f, 120f)), Vector3.Zero, 100f));
    }

    // ── Coffer tiers ──

    [Theory]
    [InlineData(1596u, TreasureTier.Bronze)]
    [InlineData(1597u, TreasureTier.Silver)]
    [InlineData(1598u, TreasureTier.Gold)]
    [InlineData(0u, TreasureTier.Unknown)]
    [InlineData(9999u, TreasureTier.Unknown)]
    public void TierFromSceneryId_MapsKnownModels(uint sceneryId, TreasureTier expected)
    {
        Assert.Equal(expected, WorldLineSelector.TierFromSceneryId(sceneryId));
    }

    // ── Label diagnostic ──

    [Theory]
    [InlineData(ObjectKind.Treasure)]
    [InlineData(ObjectKind.EventObj)]
    [InlineData(ObjectKind.EventNpc)]
    [InlineData(ObjectKind.Aetheryte)]
    public void IsLabelCandidate_IncludesNonCreatureKinds(ObjectKind kind)
    {
        Assert.True(WorldLineSelector.IsLabelCandidate(kind));
    }

    [Theory]
    [InlineData(ObjectKind.Pc)]
    [InlineData(ObjectKind.BattleNpc)]
    [InlineData(ObjectKind.Companion)]
    [InlineData(ObjectKind.None)]
    public void IsLabelCandidate_ExcludesCreatureKinds(ObjectKind kind)
    {
        Assert.False(WorldLineSelector.IsLabelCandidate(kind));
    }
}
