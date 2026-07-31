using System.Collections.Generic;

namespace Daedalus.Data;

/// <summary>
/// Phantom Jobs — the Occult Crescent duty-action layer (South Horn 7.25, North Horn 7.55).
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

    // North Horn block (7.55+, status ids 5328–5335)
    PhantomNinja,
    PhantomWhiteMage,
    PhantomBlackMage,
    PhantomDragoon,
    PhantomSummoner,
    PhantomBlueMage,
    PhantomRedMage,
    Necromancer,
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
        // North Horn (7.55): status block 5328–5335, XIVAPI-enumerated 2026-07-30;
        // Necromancer field-confirmed live (status-gain flytext "+ Phantom Necromancer").
        new(PhantomJob.PhantomNinja,     5328u),
        new(PhantomJob.PhantomWhiteMage, 5329u),
        new(PhantomJob.PhantomBlackMage, 5330u),
        new(PhantomJob.PhantomDragoon,   5331u),
        new(PhantomJob.PhantomSummoner,  5332u),
        new(PhantomJob.PhantomBlueMage,  5333u),
        new(PhantomJob.PhantomRedMage,   5334u),
        new(PhantomJob.Necromancer,      5335u),
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
        // North Horn (field 2026-07-30/31): shop prices read off the Currency Exchange.
        PhantomJob.Necromancer => "Soul Stone drop — Critical Encounter: Dark Artistry (North Horn)",
        PhantomJob.PhantomNinja or PhantomJob.PhantomWhiteMage or PhantomJob.PhantomBlackMage
            or PhantomJob.PhantomRedMage =>
            "Soul Shard — North Horn Currency Exchange, 1,000 E. Silver Obols",
        PhantomJob.PhantomDragoon or PhantomJob.PhantomSummoner =>
            "Soul Shard — North Horn Currency Exchange, 1,600 E. Gold Obols",
        PhantomJob.PhantomBlueMage => "North Horn — source not yet confirmed",
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
    /// <summary>
    /// The North Horn roster (7.55). Their shards are sold for OBOLS at the North Horn
    /// exchange, so the affordable-shard banner must never offer them against a South Horn
    /// Pieces balance (or vice versa) — see <see cref="IsSoldIn"/>.
    /// </summary>
    public static readonly IReadOnlySet<PhantomJob> NorthHornJobs = new HashSet<PhantomJob>
    {
        PhantomJob.PhantomNinja, PhantomJob.PhantomWhiteMage, PhantomJob.PhantomBlackMage,
        PhantomJob.PhantomDragoon, PhantomJob.PhantomSummoner, PhantomJob.PhantomBlueMage,
        PhantomJob.PhantomRedMage, PhantomJob.Necromancer,
    };

    /// <summary>Whether this job's soul shard is obtainable in the given Occult territory.</summary>
    public static bool IsSoldIn(PhantomJob job, ushort territoryId) =>
        territoryId == NorthHornTerritoryId ? NorthHornJobs.Contains(job) : !NorthHornJobs.Contains(job);

    /// <summary>Which Occult zone a phantom job belongs to (its roster, not its shop).</summary>
    public static bool IsNorthHornJob(PhantomJob job) => NorthHornJobs.Contains(job);

    /// <summary>Display name for an Occult territory.</summary>
    public static string GetZoneName(ushort territoryId) => territoryId switch
    {
        SouthHornTerritoryId => "South Horn",
        NorthHornTerritoryId => "North Horn",
        _ => "Occult Crescent",
    };

    public static (UnlockKind Kind, int Price) GetUnlockCost(PhantomJob job) => job switch
    {
        PhantomJob.Freelancer => (UnlockKind.Default, 0),
        PhantomJob.Knight or PhantomJob.Monk or PhantomJob.Bard => (UnlockKind.Quest, 0),
        PhantomJob.TimeMage or PhantomJob.Cannoneer or PhantomJob.Chemist
            or PhantomJob.MysticKnight or PhantomJob.Dancer => (UnlockKind.SilverShard, SilverShardPrice),
        PhantomJob.Samurai or PhantomJob.Geomancer or PhantomJob.Thief
            or PhantomJob.Gladiator => (UnlockKind.GoldShard, GoldShardPrice),
        // North Horn exchange — same price tiers, paid in Obols. The 1,000 tier is
        // field-confirmed (shop screenshot, Silver Obol balance); the 1,600 tier follows the
        // South Horn pattern and is pending a Gold Obol balance to confirm.
        PhantomJob.PhantomNinja or PhantomJob.PhantomWhiteMage or PhantomJob.PhantomBlackMage
            or PhantomJob.PhantomRedMage => (UnlockKind.SilverShard, SilverShardPrice),
        PhantomJob.PhantomDragoon or PhantomJob.PhantomSummoner => (UnlockKind.GoldShard, GoldShardPrice),
        _ => (UnlockKind.CriticalEncounter, 0),
    };

    /// <summary>
    /// Purchasable soul shards the character can afford RIGHT NOW for jobs still locked
    /// (level 0 in <paramref name="jobLevels"/>). Pure — feeds the zone HUD banner.
    /// </summary>
    public static List<(PhantomJob Job, UnlockKind Kind, int Price)> GetAffordableLockedShards(
        IReadOnlyDictionary<PhantomJob, byte> jobLevels, uint silver, uint gold, ushort territoryId)
    {
        var result = new List<(PhantomJob, UnlockKind, int)>();
        foreach (var entry in LevelStatuses)
        {
            if (!jobLevels.TryGetValue(entry.Key, out var level) || level > 0)
                continue;
            // Only what THIS zone's exchange sells — the balances are that zone's currency.
            if (!IsSoldIn(entry.Key, territoryId))
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
