using Daedalus.Data;
using Daedalus.Services.Positional;
using Daedalus.Services.Positional.Navigation;
using Xunit;

namespace Daedalus.Tests.Services.Positional.Navigation;

/// <summary>
/// Tests for the BMR AI auto-manage policy: role → stand distance, and Daedalus's next-GCD positional →
/// BMR's Positional enum name (the dynamic-positional improvement over a single static value).
/// </summary>
public sealed class BmrAiConfigPolicyTests
{
    [Theory]
    [InlineData(JobRegistry.WhiteMage, true)]
    [InlineData(JobRegistry.Sage, true)]
    [InlineData(JobRegistry.Bard, true)]
    [InlineData(JobRegistry.BlackMage, true)]
    [InlineData(JobRegistry.Samurai, false)]
    [InlineData(JobRegistry.Paladin, false)]
    public void IsBacklineJob_ClassifiesRoles(uint jobId, bool expected) =>
        Assert.Equal(expected, BmrAiConfigPolicy.IsBacklineJob(jobId));

    [Fact]
    public void ResolveMaxDistance_Backline_UsesRangedDistance()
    {
        Assert.Equal(15f, BmrAiConfigPolicy.ResolveMaxDistance(JobRegistry.WhiteMage, 15f));
        Assert.Equal(12f, BmrAiConfigPolicy.ResolveMaxDistance(JobRegistry.BlackMage, 12f));
    }

    [Fact]
    public void ResolveMaxDistance_Melee_HugsTheTarget()
    {
        Assert.Equal(BmrAiConfigPolicy.MeleeStandDistance, BmrAiConfigPolicy.ResolveMaxDistance(JobRegistry.Samurai, 15f));
        Assert.Equal(BmrAiConfigPolicy.MeleeStandDistance, BmrAiConfigPolicy.ResolveMaxDistance(JobRegistry.Paladin, 15f));
    }

    [Theory]
    [InlineData(PositionalType.Rear, "Rear")]
    [InlineData(PositionalType.Flank, "Flank")]
    [InlineData(PositionalType.Front, "Front")]
    public void ResolveDesiredPositional_Melee_FollowsNextGcd(PositionalType required, string expected) =>
        Assert.Equal(expected, BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Reaper, required, boundaryCampingActive: false));

    [Fact]
    public void ResolveDesiredPositional_Melee_NoRequirement_IsAny() =>
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Reaper, null, boundaryCampingActive: false));

    [Fact]
    public void ResolveDesiredPositional_Backline_AlwaysAny()
    {
        // Backline jobs have no positionals — never force one even if a value slips through.
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.WhiteMage, PositionalType.Rear, boundaryCampingActive: false));
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Bard, null, boundaryCampingActive: false));
    }

    [Theory]
    [InlineData(PositionalType.Rear)]
    [InlineData(PositionalType.Flank)]
    [InlineData(PositionalType.Front)]
    public void ResolveDesiredPositional_MeleeCamping_ReturnsAny(PositionalType required) =>
        // Boundary camping live: Daedalus owns the angle via positional arcs, BMR only keeps range —
        // a pushed positional would have BMR fight us over the standing angle.
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Ninja, required, boundaryCampingActive: true));

    [Fact]
    public void ResolveDesiredPositional_MeleeNotCamping_KeepsLivePositional() =>
        Assert.Equal("Rear", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.Ninja, PositionalType.Rear, boundaryCampingActive: false));

    [Fact]
    public void ResolveDesiredPositional_BacklineCamping_StillAny() =>
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(JobRegistry.WhiteMage, PositionalType.Rear, boundaryCampingActive: true));

    [Theory]
    [InlineData(PositionalType.Rear)]
    [InlineData(PositionalType.Flank)]
    public void ResolveDesiredPositional_ForbiddenZonesLive_ReturnsAny(PositionalType required) =>
        // Field report 2026-07-26 (NIN ate point-blanks): BMR's positional-goal mode pins a
        // 2.6y goal ring in the required arc — inside boss-centered AoEs. While any forbidden
        // zone is live the positional preference clears so the pathfinder flees unbiased.
        Assert.Equal("Any", BmrAiConfigPolicy.ResolveDesiredPositional(
            JobRegistry.Ninja, required, boundaryCampingActive: false, forbiddenZonesLive: true));

    [Fact]
    public void ResolveDesiredPositional_ZonesCleared_PositionalReasserts() =>
        Assert.Equal("Rear", BmrAiConfigPolicy.ResolveDesiredPositional(
            JobRegistry.Ninja, PositionalType.Rear, boundaryCampingActive: false, forbiddenZonesLive: false));

    // ── "Daedalus" preset JSON (schema per AutoDuty's field-proven presets) ────────────

    [Fact]
    public void PresetJson_Melee_HugsTarget_WithPositionalSlot()
    {
        var json = BmrAiConfigPolicy.BuildPresetJson(backline: false, rangedDistance: 15f);

        Assert.Contains("\"Name\": \"Daedalus\"", json);
        Assert.Contains("BossMod.Autorotation.MiscAI.StayCloseToTarget", json);
        Assert.Contains(BmrAiConfigPolicy.GoToPositionalModule, json);
        Assert.Contains("\"Option\": \"Pathfind\"", json);
        Assert.DoesNotContain("StayCloseToPartyRole", json);
    }

    [Fact]
    public void PresetJson_Backline_HoldsRange_NoPositional()
    {
        var json = BmrAiConfigPolicy.BuildPresetJson(backline: true, rangedDistance: 15f);

        Assert.Contains("BossMod.Autorotation.MiscAI.StayCloseToPartyRole", json);
        Assert.Contains("\"Option\": \"15\"", json);
        Assert.DoesNotContain("GoToPositional", json);
        Assert.DoesNotContain("StayCloseToTarget", json);
    }

    [Fact]
    public void PresetJson_IsWellFormedJson()
    {
        foreach (var backline in new[] { true, false })
        {
            var json = BmrAiConfigPolicy.BuildPresetJson(backline, 20f);
            var parsed = Newtonsoft.Json.Linq.JObject.Parse(json); // throws on malformed JSON
            Assert.Equal("Daedalus", parsed["Name"]!.ToString());
            Assert.NotEmpty((Newtonsoft.Json.Linq.JObject)parsed["Modules"]!);
        }
    }

    // ── AI-mode tracking via BMR's "bmr-ai" status-bar entry ────────────────
    // BMR has no "is AI enabled" IPC; the DTR entry text ("AI: On"/"AI: Off") is the only
    // published truth. A hidden or empty entry means UNKNOWN — never Off (BMR only writes the
    // text while its "Show DTR" toggle is on, so absence proves nothing).

    [Theory]
    [InlineData(true, "AI: On", BmrAiConfigService.BmrAiMode.On)]
    [InlineData(true, "AI: Off", BmrAiConfigService.BmrAiMode.Off)]
    public void ParseAiDtr_ReadsBmrStates(bool shown, string text, BmrAiConfigService.BmrAiMode expected) =>
        Assert.Equal(expected, BmrAiConfigService.ParseAiDtr(shown, text));

    [Fact]
    public void ParseAiDtr_HiddenEntry_IsUnknown_NotOff() =>
        Assert.Equal(BmrAiConfigService.BmrAiMode.Unknown, BmrAiConfigService.ParseAiDtr(shown: false, text: "AI: On"));

    [Fact]
    public void ParseAiDtr_EmptyOrForeignText_IsUnknown()
    {
        Assert.Equal(BmrAiConfigService.BmrAiMode.Unknown, BmrAiConfigService.ParseAiDtr(shown: true, text: null));
        Assert.Equal(BmrAiConfigService.BmrAiMode.Unknown, BmrAiConfigService.ParseAiDtr(shown: true, text: ""));
        Assert.Equal(BmrAiConfigService.BmrAiMode.Unknown, BmrAiConfigService.ParseAiDtr(shown: true, text: "something else"));
    }

    // Contested-slot ownership (field 2026-07-27: the yield tripped on an EMPTY active preset
    // right after enable — a cleared slot isn't contention, only a named foreign preset is).

    [Fact]
    public void CountsAsForeignOwner_EmptyOrNull_IsNotContention()
    {
        Assert.False(BmrAiConfigPolicy.CountsAsForeignOwner(""));
        Assert.False(BmrAiConfigPolicy.CountsAsForeignOwner(null));
    }

    [Fact]
    public void CountsAsForeignOwner_OurOwnPreset_IsNotContention() =>
        Assert.False(BmrAiConfigPolicy.CountsAsForeignOwner(BmrAiConfigPolicy.PresetName));

    [Fact]
    public void CountsAsForeignOwner_NamedForeignPreset_IsContention()
    {
        Assert.True(BmrAiConfigPolicy.CountsAsForeignOwner("passive - melee"));
        Assert.True(BmrAiConfigPolicy.CountsAsForeignOwner("AutoDuty"));
    }
}
