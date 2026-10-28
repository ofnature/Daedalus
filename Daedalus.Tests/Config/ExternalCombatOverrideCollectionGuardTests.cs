using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Daedalus.Tests.Config;

/// <summary>
/// <see cref="Configuration.ExternalCombatOverride"/> lives in a process-wide static, and xUnit runs test
/// classes in parallel. Every class that writes it must sit in the "ExternalCombatOverrideState"
/// collection, or a test that sets the override and then reads it can see a write from another class.
/// <para>
/// Writes need not look like an assignment. <c>ConfigurationCopier</c> copies every public property,
/// this one included, so each copy reads the static and writes it back; <c>DutyConfigurationService</c>
/// copies when built and on every Refresh. A copy that read <c>false</c> just before a melee engage test
/// set <c>true</c>, and wrote it just after, made the Reaper module see no override and queue nothing.
/// </para>
/// </summary>
public sealed class ExternalCombatOverrideCollectionGuardTests
{
    [Fact]
    public void EveryTestClassThatWritesTheOverride_IsInItsCollection()
    {
        var root = FindTestsRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var source = File.ReadAllText(file);
            if (!Writer.IsMatch(source))
                continue;

            var classes = TopLevelClass.Matches(source);
            if (classes.Count == 0)
            {
                offenders.Add($"{Path.GetFileName(file)} (no top-level class found)");
                continue;
            }

            for (var i = 0; i < classes.Count; i++)
            {
                var start = classes[i].Index;
                var end = i + 1 < classes.Count ? classes[i + 1].Index : source.Length;
                if (Writer.IsMatch(source[start..end]) && !classes[i].Groups["attrs"].Value.Contains(CollectionAttribute))
                    offenders.Add($"{Path.GetFileName(file)}: {classes[i].Groups["name"].Value}");
            }
        }

        Assert.Empty(offenders);
    }

    // Built at runtime so this file doesn't match its own search.
    private static readonly string CollectionAttribute = "[Collection(\"" + "ExternalCombatOverrideState" + "\")]";

    // Direct writes, the config copier, and the production classes whose constructors or Dispose
    // write the override. QuestionableIpc's nested FreshPullSettleGate does not match.
    private static readonly Regex Writer = new(string.Join("|",
        @"\bExternalCombatOverride" + @"\s*=[^=]",
        @"\bExternalCombatOverrideState" + @"\.(?:Active|Source)\s*=[^=]",
        @"\bConfigurationCopier" + @"\.",
        @"\bnew\s+" + "DutyConfigurationService" + @"\s*\(",
        @"\bnew\s+" + "RsrCompatIpc" + @"\s*\(",
        @"\bnew\s+" + "QuestionableIpc" + @"\s*\(",
        @"\bnew\s+" + "AutomationBusyBridge" + @"\s*\(",
        @"\bnew\s+" + "FarmModeService" + @"\s*\("));

    // A class declared at column 0 (test files use file-scoped namespaces), with the attribute lines
    // directly above it. Nested helper classes are indented, so they count as part of their outer class.
    private static readonly Regex TopLevelClass = new(
        @"(?<attrs>(?:^\[.*\]\r?\n)*)^(?:(?:public|internal|sealed|static|abstract|partial)\s+)*class\s+(?<name>\w+)",
        RegexOptions.Multiline);

    private static string FindTestsRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Daedalus.Tests.csproj")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
