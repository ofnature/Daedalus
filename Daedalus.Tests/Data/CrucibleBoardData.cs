using System.Collections.Generic;
using System.Numerics;
using Daedalus.Data;

namespace Daedalus.Tests.Data;

/// <summary>The five Crucible boards as the client holds them (dumped 2026-10-31 with Lumina): XBMContentStageEvent,
/// XBMContentStageEventMap, XBMContentBattle, the random pools and each territory's planmap.lgb LVD_ROUTE pop points.</summary>
internal static class CrucibleBoardData
{
    public static readonly CrucibleStageRow[] Rows1 =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 2, 2), new(2, 2, 3), new(3, 6, 0), new(3, 7, 0), new(4, 2, 4), new(5, 7, 1), new(6, 3, 5), new(7, 7, 2), new(7, 6, 1), new(8, 5, 0), new(9, 4, 0),
    ];

    public static readonly CrucibleMapRow[] Map1 =
    [
        new(6, 3, 1, 12, 0), new(6, 4, 6, 11, 12), new(6, 4, 1, 11, 0), new(7, 5, 9, 10, 11), new(7, 5, 1, 10, 0), new(5, 5, 10, 9, 11), new(5, 5, 1, 9, 0), new(6, 6, 10, 8, 10), new(6, 6, 9, 8, 9), new(6, 6, 1, 8, 0), new(6, 7, 6, 7, 8), new(6, 7, 1, 7, 0), new(6, 8, 6, 6, 7), new(6, 8, 1, 6, 0), new(7, 9, 9, 5, 6), new(7, 9, 1, 5, 0), new(5, 9, 10, 4, 6), new(5, 9, 1, 4, 0), new(7, 10, 6, 3, 5), new(7, 10, 1, 3, 0), new(5, 10, 6, 2, 4), new(5, 10, 1, 2, 0), new(6, 11, 10, 1, 3), new(6, 11, 9, 1, 2), new(6, 11, 1, 1, 0), new(6, 12, 6, 0, 1), new(6, 12, 1, 0, 0),
    ];

    public static readonly int[] Battles1 = [6, 1, 2, 3, 4, 5];

    public static readonly IReadOnlyList<IReadOnlyList<(byte Type, int Index)>> Random1 =
    [
        
    ];

    public static readonly Vector3[] Points1 =
    [
        new(-700f, 0f, -75f), new(-700f, 0f, -66f), new(-695f, 0f, -58.5f), new(-705f, 0f, -58.5f), new(-700f, 0f, -51f), new(-700f, 0f, -42f), new(-700f, 0f, -33f), new(-695f, 0f, -25.5f), new(-705f, 0f, -25.5f), new(-695f, 0f, -16.5f), new(-705f, 0f, -16.5f), new(-700f, 0f, -9f),
    ];

    public static readonly CrucibleStageRow[] Rows2 =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 5, 0), new(3, 3, 2), new(3, 2, 3), new(4, 7, 0), new(4, 6, 0), new(5, 2, 4), new(6, 7, 1), new(7, 3, 5), new(8, 8, 0), new(8, 7, 2), new(9, 5, 1), new(10, 4, 0),
    ];

    public static readonly CrucibleMapRow[] Map2 =
    [
        new(6, 2, 1, 13, 0), new(6, 3, 6, 12, 13), new(6, 3, 1, 12, 0), new(7, 4, 4, 11, 12), new(7, 4, 1, 11, 0), new(5, 4, 5, 10, 12), new(5, 4, 1, 10, 0), new(6, 5, 5, 9, 11), new(6, 5, 4, 9, 10), new(6, 5, 1, 9, 0), new(6, 6, 6, 8, 9), new(6, 6, 1, 8, 0), new(6, 7, 6, 7, 8), new(6, 7, 1, 7, 0), new(7, 8, 4, 6, 7), new(7, 8, 1, 6, 0), new(5, 8, 5, 5, 7), new(5, 8, 1, 5, 0), new(7, 9, 6, 4, 6), new(7, 9, 1, 4, 0), new(5, 9, 6, 3, 5), new(5, 9, 1, 3, 0), new(6, 10, 5, 2, 4), new(6, 10, 4, 2, 3), new(6, 10, 1, 2, 0), new(6, 11, 6, 1, 2), new(6, 11, 1, 1, 0), new(6, 12, 6, 0, 1), new(6, 12, 1, 0, 0),
    ];

    public static readonly int[] Battles2 = [13, 7, 8, 9, 10, 11, 12];

    public static readonly IReadOnlyList<IReadOnlyList<(byte Type, int Index)>> Random2 =
    [
        new List<(byte, int)> { (2, 6), (6, 1) },
    ];

    public static readonly Vector3[] Points2 =
    [
        new(-700f, 0f, -9f), new(-695f, 0f, -25.5f), new(-700f, 0f, -42f), new(-695f, 0f, -34.5f), new(-705f, 0f, -34.5f), new(-700f, 0f, -51f), new(-705f, 0f, -25.5f), new(-695f, 0f, -67.5f), new(-700f, 0f, -18f), new(-700f, 0f, -84f), new(-700f, 0f, -60f), new(-705f, 0f, -67.5f), new(-700f, 0f, -75f),
    ];

    public static readonly CrucibleStageRow[] Rows3 =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 2, 2), new(3, 5, 0), new(4, 3, 3), new(4, 2, 4), new(5, 7, 0), new(5, 6, 0), new(6, 2, 5), new(7, 7, 1), new(7, 5, 1), new(8, 3, 6), new(9, 7, 2), new(10, 8, 0), new(10, 6, 1), new(11, 5, 2), new(12, 4, 0),
    ];

    public static readonly CrucibleMapRow[] Map3 =
    [
        new(6, 0, 1, 16, 0), new(6, 1, 6, 15, 16), new(6, 1, 1, 15, 0), new(7, 2, 9, 14, 15), new(7, 2, 1, 14, 0), new(5, 2, 10, 13, 15), new(5, 2, 1, 13, 0), new(6, 3, 10, 12, 14), new(6, 3, 9, 12, 13), new(6, 3, 1, 12, 0), new(6, 4, 6, 11, 12), new(6, 4, 1, 11, 0), new(7, 5, 9, 10, 11), new(7, 5, 1, 10, 0), new(5, 5, 10, 9, 11), new(5, 5, 1, 9, 0), new(6, 6, 10, 8, 10), new(6, 6, 9, 8, 9), new(6, 6, 1, 8, 0), new(7, 7, 9, 7, 8), new(7, 7, 1, 7, 0), new(5, 7, 10, 6, 8), new(5, 7, 1, 6, 0), new(7, 8, 6, 5, 7), new(7, 8, 1, 5, 0), new(5, 8, 6, 4, 6), new(5, 8, 1, 4, 0), new(6, 9, 10, 3, 5), new(6, 9, 9, 3, 4), new(6, 9, 1, 3, 0), new(6, 10, 6, 2, 3), new(6, 10, 1, 2, 0), new(6, 11, 6, 1, 2), new(6, 11, 1, 1, 0), new(6, 12, 6, 0, 1), new(6, 12, 1, 0, 0),
    ];

    public static readonly int[] Battles3 = [21, 14, 15, 16, 17, 18, 19, 20];

    public static readonly IReadOnlyList<IReadOnlyList<(byte Type, int Index)>> Random3 =
    [
        new List<(byte, int)> { (2, 7), (7, 3) },
    ];

    public static readonly Vector3[] Points3 =
    [
        new(-700f, 0f, -90f), new(-705f, 0f, -82.5f), new(-700f, 0f, -66f), new(-695f, 0f, -58.5f), new(-700f, 0f, -51f), new(-700f, 0f, -99f), new(-700f, 0f, -27f), new(-695f, 0f, -82.5f), new(-700f, 0f, -75f), new(-705f, 0f, -34.5f), new(-705f, 0f, -58.5f), new(-705f, 0f, -43.5f), new(-695f, 0f, -43.5f), new(-695f, 0f, -34.5f), new(-700f, 0f, -18f), new(-700f, 0f, -9f),
    ];

    public static readonly CrucibleStageRow[] Rows4 =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 2, 2), new(2, 8, 0), new(3, 5, 0), new(4, 6, 0), new(4, 8, 1), new(5, 3, 5), new(6, 8, 2), new(6, 7, 3), new(7, 3, 7), new(7, 2, 8), new(8, 8, 3), new(8, 5, 2), new(9, 4, 0),
    ];

    public static readonly CrucibleMapRow[] Map4 =
    [
        new(6, 3, 1, 14, 0), new(7, 4, 9, 13, 14), new(7, 4, 1, 13, 0), new(5, 4, 10, 12, 14), new(5, 4, 1, 12, 0), new(7, 5, 6, 11, 13), new(7, 5, 1, 11, 0), new(5, 5, 6, 10, 12), new(5, 5, 1, 10, 0), new(7, 6, 6, 9, 11), new(7, 6, 1, 9, 0), new(5, 6, 6, 8, 10), new(5, 6, 1, 8, 0), new(6, 7, 10, 7, 9), new(6, 7, 9, 7, 8), new(6, 7, 1, 7, 0), new(7, 8, 9, 6, 7), new(7, 8, 1, 6, 0), new(5, 8, 10, 5, 7), new(5, 8, 1, 5, 0), new(6, 9, 10, 4, 6), new(6, 9, 9, 4, 5), new(6, 9, 1, 4, 0), new(7, 10, 9, 3, 4), new(7, 10, 1, 3, 0), new(5, 10, 10, 2, 4), new(5, 10, 1, 2, 0), new(6, 11, 10, 1, 3), new(6, 11, 9, 1, 2), new(6, 11, 1, 1, 0), new(6, 12, 6, 0, 1), new(6, 12, 1, 0, 0),
    ];

    public static readonly int[] Battles4 = [31, 22, 23, 24, 25, 26, 27, 28, 29, 30];

    public static readonly IReadOnlyList<IReadOnlyList<(byte Type, int Index)>> Random4 =
    [
        new List<(byte, int)> { (2, 3), (7, 0) },
        new List<(byte, int)> { (2, 4), (7, 1) },
        new List<(byte, int)> { (2, 6), (7, 2), (6, 1) },
        new List<(byte, int)> { (2, 9), (5, 1) },
    ];

    public static readonly Vector3[] Points4 =
    [
        new(-695f, 0f, -64.5f), new(-700f, 0f, -9f), new(-705f, 0f, -16.5f), new(-695f, 0f, -55.5f), new(-705f, 0f, -31.5f), new(-695f, 0f, -46.5f), new(-700f, 0f, -39f), new(-700f, 0f, -24f), new(-700f, 0f, -72f), new(-695f, 0f, -16.5f), new(-695f, 0f, -31.5f), new(-705f, 0f, -46.5f), new(-705f, 0f, -55.5f), new(-705f, 0f, -64.5f),
    ];

    public static readonly CrucibleStageRow[] Rows5 =
    [
        new(0, 1, -1), new(1, 2, 1), new(2, 3, 2), new(2, 5, 0), new(3, 6, 0), new(3, 7, 0), new(3, 8, 0), new(4, 8, 1), new(4, 2, 5), new(4, 7, 2), new(5, 3, 6), new(6, 6, 1), new(7, 8, 2), new(7, 5, 2), new(8, 7, 4), new(8, 7, 5), new(9, 3, 8), new(9, 2, 9), new(10, 2, 10), new(11, 5, 3), new(12, 8, 3), new(12, 6, 2), new(12, 7, 7), new(13, 8, 4), new(13, 3, 13), new(14, 4, 0),
    ];

    public static readonly CrucibleMapRow[] Map5 =
    [
        new(6, 3, 1, 25, 0), new(7, 4, 9, 24, 25), new(7, 4, 1, 24, 0), new(5, 4, 10, 23, 25), new(5, 4, 1, 23, 0), new(7, 5, 6, 22, 24), new(7, 5, 1, 22, 0), new(6, 4, 6, 21, 25), new(6, 4, 1, 21, 0), new(5, 5, 6, 20, 23), new(5, 5, 1, 20, 0), new(6, 5, 8, 19, 22), new(6, 5, 6, 19, 21), new(6, 5, 7, 19, 20), new(6, 5, 1, 19, 0), new(6, 6, 6, 18, 19), new(6, 6, 1, 18, 0), new(7, 6, 7, 17, 18), new(8, 6, 7, 17, 18), new(8, 6, 1, 17, 0), new(5, 6, 8, 16, 18), new(4, 6, 8, 16, 18), new(4, 6, 1, 16, 0), new(8, 7, 6, 15, 17), new(8, 7, 1, 15, 0), new(4, 7, 6, 14, 16), new(4, 7, 1, 14, 0), new(7, 7, 8, 13, 15), new(7, 7, 1, 13, 0), new(5, 7, 7, 12, 14), new(5, 7, 1, 12, 0), new(6, 7, 8, 11, 13), new(6, 7, 7, 11, 12), new(6, 7, 1, 11, 0), new(6, 8, 6, 10, 11), new(6, 8, 1, 10, 0), new(7, 9, 9, 9, 10), new(7, 9, 1, 9, 0), new(6, 9, 6, 8, 10), new(6, 9, 1, 8, 0), new(5, 9, 10, 7, 10), new(5, 9, 1, 7, 0), new(7, 10, 6, 6, 9), new(7, 10, 1, 6, 0), new(6, 10, 6, 5, 8), new(6, 10, 1, 5, 0), new(5, 10, 6, 4, 7), new(5, 10, 1, 4, 0), new(7, 11, 6, 3, 6), new(7, 11, 9, 3, 5), new(7, 11, 1, 3, 0), new(5, 11, 10, 2, 5), new(5, 11, 6, 2, 4), new(5, 11, 1, 2, 0), new(6, 11, 8, 1, 3), new(6, 11, 7, 1, 2), new(6, 11, 1, 1, 0), new(6, 12, 6, 0, 1), new(6, 12, 1, 0, 0), new(0, 0, 0, 0, 0), new(0, 0, 0, 0, 0),
    ];

    public static readonly int[] Battles5 = [45, 32, 33, 34, 35, 36, 37, 38, 39, 40, 41, 42, 43, 44];

    public static readonly IReadOnlyList<IReadOnlyList<(byte Type, int Index)>> Random5 =
    [
        new List<(byte, int)> { (2, 3), (7, 1) },
        new List<(byte, int)> { (2, 4), (5, 1) },
        new List<(byte, int)> { (3, 7), (7, 3) },
        new List<(byte, int)> { (2, 11), (7, 6) },
        new List<(byte, int)> { (3, 12), (5, 4) },
    ];

    public static readonly Vector3[] Points5 =
    [
        new(-691f, 0f, -72f), new(-700f, 0f, -9f), new(-700f, 0f, -27f), new(-682f, 0f, -54f), new(-709f, 0f, -18f), new(-700f, 0f, -18f), new(-691f, 0f, -27f), new(-709f, 0f, -9f), new(-718f, 0f, -45f), new(-700f, 0f, -45f), new(-691f, 0f, -9f), new(-700f, 0f, -81f), new(-691f, 0f, -18f), new(-709f, 0f, -27f), new(-700f, 0f, -36f), new(-709f, 0f, -45f), new(-691f, 0f, -45f), new(-682f, 0f, -45f), new(-718f, 0f, -54f), new(-700f, 0f, -54f), new(-700f, 0f, -63f), new(-709f, 0f, -63f), new(-691f, 0f, -63f), new(-709f, 0f, -72f), new(-700f, 0f, -72f),
    ];

}
