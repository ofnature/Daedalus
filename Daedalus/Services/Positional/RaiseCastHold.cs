using System;

namespace Daedalus.Services.Positional;

/// <summary>
/// STATIC-BACKED movement hold for hardcast raises (alliance-raid field report 2026-07-19:
/// LAN party WAR/SAM/SGE/PCT — one raiser, hardcast ENABLED, and the raise still never
/// happened. Root cause: BMR AI micro-moves the toon near-constantly, so the hardcast
/// branch's !isMoving gate read "Moving (can't hardcast)" for the whole fight).
/// A raise module requests the hold; the Plugin pump pauses BMR AI movement
/// (BossMod.AI.PauseMovement) while it's active and resumes on expiry — the toon stops,
/// the 8-10s cast goes out, the hold lapses. Expiry-driven so a crash or an abandoned
/// raise can never leave movement paused. NOTE: only BMR-driven movement is paused; a
/// vNav path of our own (farm follow etc.) keeps running and the hold simply expires.
/// </summary>
public static class RaiseCastHold
{
    private static DateTime _holdUntilUtc = DateTime.MinValue;

    /// <summary>A raiser wants the toon stationary for a hardcast (extend freely per frame).</summary>
    public static void Request(float seconds)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        if (until > _holdUntilUtc)
            _holdUntilUtc = until;
    }

    public static bool Active => DateTime.UtcNow < _holdUntilUtc;

    public static void Clear() => _holdUntilUtc = DateTime.MinValue;
}
