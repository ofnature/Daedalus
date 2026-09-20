using Daedalus.Data;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Pure decision logic for auto-managing BossMod Reborn's AI movement config by role: how far each role
/// stands from the target, and which positional to feed BMR for the next melee GCD. Extracted so it can
/// be unit-tested without the BMR IPC.
/// </summary>
public static class BmrAiConfigPolicy
{
    /// <summary>BMR's default melee stand distance (hug the hitbox).</summary>
    public const float MeleeStandDistance = 2.6f;

    /// <summary>Healers, ranged physical, and caster DPS — the jobs that should hold at range.</summary>
    public static bool IsBacklineJob(uint jobId) =>
        JobRegistry.IsHealer(jobId)
        || JobRegistry.IsRangedPhysicalDps(jobId)
        || JobRegistry.IsCasterDps(jobId);

    /// <summary>Max distance from the target by role: backline holds at <paramref name="rangedDistance"/>; melee/tank hug.</summary>
    public static float ResolveMaxDistance(uint jobId, float rangedDistance) =>
        IsBacklineJob(jobId) ? rangedDistance : MeleeStandDistance;

    /// <summary>
    /// Minimum distance from the target's hitbox, by role. Without one, BMR's band has no floor: every cell from the
    /// hitbox outwards satisfies the goal equally, so a caster that ends up under the boss has no reason to leave and
    /// micro-adjusts there instead of casting (field 2026-09-19, a Pictomancer at zero). Melee keep none -- their
    /// range IS the hitbox. BMR turns a non-zero value into a donut goal around the target.
    /// </summary>
    public static float ResolveMinDistance(uint jobId, float rangedMinDistance) =>
        IsBacklineJob(jobId) ? System.Math.Clamp(rangedMinDistance, 0f, MaxRangedMinDistance) : 0f;

    /// <summary>The largest floor worth asking for: past this a backline job is giving up uptime to stand further out
    /// than any mechanic requires.</summary>
    public const float MaxRangedMinDistance = 3f;

    /// <summary>
    /// The party role a backline job holds its range off. BMR's <c>StayCloseToPartyRole</c> does nothing at all when
    /// the role is None -- which is its default -- so a preset that sets only the range says nothing, and until
    /// 2026-09-19 that was the whole of the backline preset: the 15 yalms never reached BMR and its own
    /// <c>MaxDistanceToTarget</c> (2.6) governed instead. The tank is the anchor because the tank is on the boss.
    /// </summary>
    public const string BacklineAnchorRole = "Tank";

    /// <summary>Our BMR autorotation preset name — the fleet's preset-based tooling sees us as a peer.</summary>
    public const string PresetName = "Daedalus";

    /// <summary>
    /// Whether the currently active preset counts as a foreign OWNER of the slot for
    /// contested-yield purposes. Empty/null means NO preset is active (a zone change or
    /// BMR reload cleared the slot) — nobody owns it, so reclaiming isn't ping-pong.
    /// Field 2026-07-27: the yield tripped on "" three frames after enable and parked
    /// Auto-Manage until re-toggled. Only a NAMED foreign preset contends.
    /// </summary>
    public static bool CountsAsForeignOwner(string? activePresetName) =>
        !string.IsNullOrEmpty(activePresetName) && activePresetName != PresetName;

    /// <summary>The BMR module fed the live per-GCD positional via a transient strategy.</summary>
    public const string GoToPositionalModule = "BossMod.Autorotation.MiscAI.GoToPositional";

    /// <summary>
    /// Builds the "Daedalus" BMR autorotation preset JSON (schema per AutoDuty's field-proven
    /// presets). Movement-only — no rotation modules, Daedalus fights. Melee/tanks: hug the
    /// target + pathfind + a GoToPositional slot driven live via transient strategies.
    /// Backline: hold range off the party + pathfind.
    /// </summary>
    public static string BuildPresetJson(bool backline, float rangedDistance)
    {
        var range = ((int)System.MathF.Round(rangedDistance)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var modules = backline
            ? $$"""
                    "BossMod.Autorotation.MiscAI.StayCloseToPartyRole": [
                      { "Track": "Role", "Option": "{{BacklineAnchorRole}}" },
                      { "Track": "range", "Option": "{{range}}" }
                    ],
                    "BossMod.Autorotation.MiscAI.NormalMovement": [
                      { "Track": "Destination", "Option": "Pathfind" }
                    ]
                """
            : $$"""
                    "BossMod.Autorotation.MiscAI.StayCloseToTarget": [],
                    "{{GoToPositionalModule}}": [],
                    "BossMod.Autorotation.MiscAI.NormalMovement": [
                      { "Track": "Destination", "Option": "Pathfind" }
                    ]
                """;

        return $$"""
            {
              "Name": "{{PresetName}}",
              "Modules": {
            {{modules}}
              }
            }
            """;
    }

    /// <summary>
    /// Maps Daedalus's next required positional to BMR's <c>Positional</c> enum name. Backline jobs and
    /// "no requirement" → <c>Any</c> (don't force a positional). Beats a static single positional because
    /// it follows the rotation's actual next GCD (RPR Gibbet↔Gallows, MNK forms, NIN). When boundary
    /// camping is live, melee also gets <c>Any</c>: Daedalus owns the standing angle via positional arcs,
    /// BMR only keeps range and dodges — feeding it a positional would have it fight us over the angle.
    /// While forbidden zones are live, melee also gets <c>Any</c> — BMR's positional-goal mode pins its
    /// goal ring at 2.6y in the required arc, which sits INSIDE boss-centered AoEs and drags the
    /// pathfinder toward the danger (field report 2026-07-26: NIN ate point-blanks).
    /// </summary>
    public static string ResolveDesiredPositional(uint jobId, PositionalType? requiredPositional, bool boundaryCampingActive, bool forbiddenZonesLive = false)
    {
        if (IsBacklineJob(jobId) || boundaryCampingActive || forbiddenZonesLive)
            return "Any";

        return requiredPositional switch
        {
            PositionalType.Rear => "Rear",
            PositionalType.Flank => "Flank",
            PositionalType.Front => "Front",
            _ => "Any",
        };
    }
}
