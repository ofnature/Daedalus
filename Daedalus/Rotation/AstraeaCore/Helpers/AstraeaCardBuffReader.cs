using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using Daedalus.Data;

namespace Daedalus.Rotation.AstraeaCore.Helpers;

/// <summary>One of our card buffs on a party member, for the Debug window.</summary>
public sealed record CardBuffHolder(string Target, float SecondsLeft);

/// <summary>A card and everyone carrying its buff from us right now (empty when nobody does).</summary>
public sealed record CardBuffRow(string Card, IReadOnlyList<CardBuffHolder> Holders);

/// <summary>
/// Which party member has each of our card buffs, and for how long. Only buffs WE cast count — another
/// Astrologian's Balance is not ours to track.
/// </summary>
public static class AstraeaCardBuffReader
{
    /// <summary>The six cards in the order the Debug window lists them.</summary>
    public static readonly (string Card, uint StatusId)[] Cards =
    [
        ("The Balance", ASTActions.TheBalanceStatusId),
        ("The Spear", ASTActions.TheSpearStatusId),
        ("The Arrow", ASTActions.TheArrowStatusId),
        ("The Bole", ASTActions.TheBoleStatusId),
        ("The Ewer", ASTActions.TheEwerStatusId),
        ("The Spire", ASTActions.TheSpireStatusId),
    ];

    /// <summary>One row per card, from the party's live status lists.</summary>
    public static IReadOnlyList<CardBuffRow> Read(uint ourEntityId, IEnumerable<IBattleChara> partyMembers)
    {
        var statuses = new List<(string Name, uint StatusId, uint SourceId, float SecondsLeft)>();
        foreach (var member in partyMembers)
        {
            var list = member.StatusList;
            if (list == null)
                continue;
            var name = member.Name.TextValue;
            foreach (var status in list)
                statuses.Add((name, status.StatusId, status.SourceId, status.RemainingTime));
        }
        return Build(ourEntityId, statuses);
    }

    /// <summary>The pure half of <see cref="Read"/>, for tests.</summary>
    internal static IReadOnlyList<CardBuffRow> Build(
        uint ourEntityId, IEnumerable<(string Name, uint StatusId, uint SourceId, float SecondsLeft)> statuses)
    {
        var holders = new Dictionary<uint, List<CardBuffHolder>>();
        foreach (var (name, statusId, sourceId, secondsLeft) in statuses)
        {
            if (sourceId != ourEntityId)
                continue;
            if (!holders.TryGetValue(statusId, out var list))
                holders[statusId] = list = [];
            list.Add(new CardBuffHolder(name, secondsLeft));
        }

        var rows = new List<CardBuffRow>(Cards.Length);
        foreach (var (card, statusId) in Cards)
            rows.Add(new CardBuffRow(card, holders.TryGetValue(statusId, out var list) ? list : []));
        return rows;
    }
}
