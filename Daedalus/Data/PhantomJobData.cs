using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// Phantom Jobs — the Occult Crescent (South Horn) duty-action layer.
/// The active phantom job and its level (1–6) are carried entirely by a player status:
/// each phantom job applies a permanent status whose stack count is the phantom level
/// (a stack count of 255 means "no level"). Only one phantom job is active at a time.
/// </summary>
public enum PhantomJob : byte
{
    None = 0,
    Freelancer,
    Knight,
    Berserker,
    Monk,
    Ranger,
    Samurai,
    Bard,
    Geomancer,
    TimeMage,
    Cannoneer,
    Chemist,
    Oracle,
    Thief,
    MysticKnight,
    Gladiator,
    Dancer,
}

/// <summary>
/// Static data for Occult Crescent phantom-job detection (Phase 1 of
/// docs/occult-phantom-plan.md). Status IDs verified against the RSR StatusID enum;
/// item IDs against RSR's PhantomRotation item tracking.
/// </summary>
public static class PhantomJobData
{
    /// <summary>The Occult Crescent: South Horn (patch 7.25).</summary>
    public const ushort SouthHornTerritoryId = 1252;

    /// <summary>The Occult Crescent: North Horn (XIVAPI TerritoryType 1346, field-sighted 2026-07-28).</summary>
    public const ushort NorthHornTerritoryId = 1346;

    /// <summary>
    /// Territories where the phantom layer is active. Never gate on a single zone id directly.
    /// </summary>
    public static readonly IReadOnlySet<ushort> OccultTerritoryIds = new HashSet<ushort>
    {
        SouthHornTerritoryId,
        NorthHornTerritoryId,
    };

    /// <summary>Stack count sentinel meaning "status present but no phantom level".</summary>
    public const byte NoLevelStacks = byte.MaxValue;

    /// <summary>
    /// The per-job level statuses (stacks = phantom level). Gladiator/MysticKnight/Dancer
    /// were added post-7.25 and sit in a later status-ID block.
    /// </summary>
    public static readonly IReadOnlyList<KeyValuePair<PhantomJob, uint>> LevelStatuses =
    [
        new(PhantomJob.Freelancer,   4242u),
        new(PhantomJob.Knight,       4358u),
        new(PhantomJob.Berserker,    4359u),
        new(PhantomJob.Monk,         4360u),
        new(PhantomJob.Ranger,       4361u),
        new(PhantomJob.Samurai,      4362u),
        new(PhantomJob.Bard,         4363u),
        new(PhantomJob.Geomancer,    4364u),
        new(PhantomJob.TimeMage,     4365u),
        new(PhantomJob.Cannoneer,    4366u),
        new(PhantomJob.Chemist,      4367u),
        new(PhantomJob.Oracle,       4368u),
        new(PhantomJob.Thief,        4369u),
        new(PhantomJob.MysticKnight, 4803u),
        new(PhantomJob.Gladiator,    4804u),
        new(PhantomJob.Dancer,       4805u),
    ];

    // Zone currencies are ITEMS (MKDData CurrencyItem rows) — balances come from the
    // inventory, not OccultCrescentState (its Silver field read 7628 vs a real balance
    // of 18 in the field check; only the item counts are authoritative).
    public const uint SilverPieceItemId = 45043; // Enlightenment Silver Piece (South Horn)
    public const uint GoldPieceItemId = 45044;   // Enlightenment Gold Piece (South Horn)
    public const uint SilverObolItemId = 51975;  // Enlightenment Silver Obol (North Horn)
    public const uint GoldObolItemId = 51976;    // Enlightenment Gold Obol (North Horn)

    /// <summary>
    /// The zone currency item pair for an Occult territory — North Horn mints Obols, South
    /// Horn Pieces. Unknown/other territories fall back to the South Horn pair.
    /// </summary>
    public static (uint Silver, uint Gold) CurrencyItemIds(ushort territoryId) =>
        territoryId == NorthHornTerritoryId
            ? (SilverObolItemId, GoldObolItemId)
            : (SilverPieceItemId, GoldPieceItemId);

    // Consumables the phantom actions burn (Chemist actions + Samurai Zeninage).
    // There is NO ether item: the Occult Ether ACTION consumes an Occult Potion — the one
    // item covers both the HP and MP restore actions (field-verified 2026-07-25).
    // Zeninage consumes an Occult Coffer.
    public const uint ZeninageCofferItemId = 47740;
    public const uint OccultPotionItemId = 47741;
    public const uint OccultElixirItemId = 47743;

    /// <summary>Item IDs surfaced in the Debug ▸ Occult consumables block.</summary>
    public static readonly IReadOnlyList<uint> ConsumableItemIds =
    [
        OccultPotionItemId,
        OccultElixirItemId,
        ZeninageCofferItemId,
    ];

    /// <summary>
    /// Resolves the active phantom job and level from a player status list.
    /// Pure — the game-facing service feeds it (statusId, stacks) pairs.
    /// A stack count of <see cref="NoLevelStacks"/> counts as level 0 (RSR rule);
    /// the first phantom status with a real level wins (only one is ever active).
    /// </summary>
    public static (PhantomJob Job, byte Level) ResolveActiveJob(
        IEnumerable<(uint StatusId, byte Stacks)> playerStatuses)
    {
        foreach (var (statusId, stacks) in playerStatuses)
        {
            if (stacks == 0 || stacks == NoLevelStacks)
                continue;

            foreach (var entry in LevelStatuses)
            {
                if (entry.Value == statusId)
                    return (entry.Key, stacks);
            }
        }

        return (PhantomJob.None, 0);
    }

    /// <summary>
    /// MKDSupportJob row index for a phantom job — the index into OccultCrescentState's
    /// per-job exp/level arrays. Enum order matches the sheet rows (field-verified:
    /// Cannoneer = row 9), so the mapping is a straight offset.
    /// </summary>
    public static int GetSupportJobRowIndex(PhantomJob job) => (int)job - 1;

    /// <summary>
    /// Where to unlock each phantom job (South Horn; sources verified 2026-07-25).
    /// Shown in the config UI for jobs the character has not unlocked yet.
    /// </summary>
    public static string GetUnlockHint(PhantomJob job) => job switch
    {
        PhantomJob.Freelancer => "Default — always available",
        PhantomJob.Knight or PhantomJob.Monk or PhantomJob.Bard =>
            "Quest: New Job, Old Tricks (South Horn)",
        PhantomJob.TimeMage or PhantomJob.Cannoneer or PhantomJob.Chemist
            or PhantomJob.MysticKnight or PhantomJob.Dancer =>
            "Soul Shard — Expedition Antiquarian (X:38.1 Y:7.0), 1,000 E. Silver Pieces",
        PhantomJob.Samurai or PhantomJob.Geomancer or PhantomJob.Thief
            or PhantomJob.Gladiator =>
            "Soul Shard — Expedition Antiquarian (X:38.1 Y:7.0), 1,600 E. Gold Pieces",
        PhantomJob.Oracle => "Soul Shard drop — Critical Encounter: On the Hunt",
        PhantomJob.Ranger => "Soul Shard drop — Critical Encounter: The Black Regiment",
        PhantomJob.Berserker => "Soul Shard drop — Critical Encounter: The Unbridled",
        _ => string.Empty,
    };

    /// <summary>How a phantom job is unlocked (drives the affordable-shard banner).</summary>
    public enum UnlockKind : byte
    {
        Default,
        Quest,
        SilverShard,
        GoldShard,
        CriticalEncounter,
    }

    public const int SilverShardPrice = 1000;
    public const int GoldShardPrice = 1600;

    /// <summary>Structured unlock source; Price is 0 for non-purchasable jobs.</summary>
    public static (UnlockKind Kind, int Price) GetUnlockCost(PhantomJob job) => job switch
    {
        PhantomJob.Freelancer => (UnlockKind.Default, 0),
        PhantomJob.Knight or PhantomJob.Monk or PhantomJob.Bard => (UnlockKind.Quest, 0),
        PhantomJob.TimeMage or PhantomJob.Cannoneer or PhantomJob.Chemist
            or PhantomJob.MysticKnight or PhantomJob.Dancer => (UnlockKind.SilverShard, SilverShardPrice),
        PhantomJob.Samurai or PhantomJob.Geomancer or PhantomJob.Thief
            or PhantomJob.Gladiator => (UnlockKind.GoldShard, GoldShardPrice),
        _ => (UnlockKind.CriticalEncounter, 0),
    };

    /// <summary>
    /// Purchasable soul shards the character can afford RIGHT NOW for jobs still locked
    /// (level 0 in <paramref name="jobLevels"/>). Pure — feeds the zone HUD banner.
    /// </summary>
    public static List<(PhantomJob Job, UnlockKind Kind, int Price)> GetAffordableLockedShards(
        IReadOnlyDictionary<PhantomJob, byte> jobLevels, uint silver, uint gold)
    {
        var result = new List<(PhantomJob, UnlockKind, int)>();
        foreach (var entry in LevelStatuses)
        {
            if (!jobLevels.TryGetValue(entry.Key, out var level) || level > 0)
                continue;

            var (kind, price) = GetUnlockCost(entry.Key);
            var affordable = kind switch
            {
                UnlockKind.SilverShard => silver >= price,
                UnlockKind.GoldShard => gold >= price,
                _ => false,
            };
            if (affordable)
                result.Add((entry.Key, kind, price));
        }

        return result;
    }

    /// <summary>Status ID for a phantom job's level status, or 0 for None.</summary>
    public static uint GetLevelStatusId(PhantomJob job)
    {
        foreach (var entry in LevelStatuses)
        {
            if (entry.Key == job)
                return entry.Value;
        }

        return 0;
    }
}
