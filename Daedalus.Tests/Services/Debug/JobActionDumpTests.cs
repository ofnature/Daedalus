using System.Collections.Generic;
using Daedalus.Services.Debug;
using Xunit;

namespace Daedalus.Tests.Services.Debug;

/// <summary>
/// The paste-ready dump used to harvest a job's real action data from the client's own sheets,
/// for jobs whose data has not reached XIVAPI yet. Formatting is pure, so it is tested without a
/// game attached; the sheet read is not, and is exercised by running the command.
/// </summary>
public sealed class JobActionDumpTests
{
    private static JobActionRow Row(uint id, string name, int level, string desc = "") => new(
        ActionId: id, Name: name, Level: level, CastSeconds: 0f, RecastSeconds: 2.5f,
        Range: 3, EffectRange: 0, Category: "Weaponskill", IsRoleAction: false,
        TargetsArea: false, Description: desc);

    /// <summary>
    /// An empty result must not read as "this job has no actions" — for an unreleased job the far
    /// likelier cause is that the rows are keyed differently, and a confident wrong conclusion
    /// there would stop the harvest before it started.
    /// </summary>
    [Fact]
    public void NoRows_SaysWhyRatherThanAssertingAbsence()
    {
        var text = JobActionDumpService.Format(43, "Beastmaster", []);

        Assert.Contains("No actions found", text);
        Assert.Contains("ClassJobCategory", text);
        Assert.Contains("before concluding the data is absent", text.Replace("\r", "").Replace("\n", " "));
    }

    [Fact]
    public void RowsRenderAsATableWithTheFactsNeededToBuildACatalog()
    {
        var text = JobActionDumpService.Format(43, "Beastmaster", new List<JobActionRow>
        {
            Row(40001, "Parting Blow", 15),
        });

        Assert.Contains("ClassJob 43 (Beastmaster) — 1 action(s)", text);
        Assert.Contains("40001", text);
        Assert.Contains("Parting Blow", text);
        Assert.Contains("Weaponskill", text);
    }

    /// <summary>
    /// Potencies live in the tooltip and must arrive verbatim. Paraphrasing them in the dump is
    /// how a wrong number gets laundered into a catalog.
    /// </summary>
    [Fact]
    public void TooltipsAreCarriedVerbatim()
    {
        const string tooltip = "Deals unaspected damage with a potency of 220.";
        var text = JobActionDumpService.Format(43, "Beastmaster", new List<JobActionRow>
        {
            Row(40001, "Parting Blow", 15, tooltip),
        });

        Assert.Contains(tooltip, text);
        Assert.Contains("transcribe, do not paraphrase", text);
    }

    /// <summary>A row with no tooltip must not emit an empty stanza.</summary>
    [Fact]
    public void RowsWithoutTooltipsAreSkippedInTheTooltipSection()
    {
        var text = JobActionDumpService.Format(43, "Beastmaster", new List<JobActionRow>
        {
            Row(40001, "Has Tooltip", 15, "Something."),
            Row(40002, "No Tooltip", 20),
        });

        Assert.Contains("40001 Has Tooltip:", text);
        Assert.DoesNotContain("40002 No Tooltip:", text);
    }
}
