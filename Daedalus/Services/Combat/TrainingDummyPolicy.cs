using System;

namespace Daedalus.Services.Combat;

/// <summary>
/// Whether automation-driven combat may fight a striking dummy.
/// <para>
/// The guard against it came with the Questionable integration: a quest plugin that lands on a dummy waits for a kill
/// that never comes, so Daedalus drops the target and ends the override. But Odysseus — ours — targets dummies on
/// purpose: job quests send you to one (Beastmaster: "Use your opo-opo's Tempered Release on the dummy", "Execute
/// Quelling Wave on the dummy"), and with the guard Daedalus dropped the dummy every time Odysseus picked it (field,
/// 2026-10-23). Odysseus finishes the step and moves on, so the never-ending fight the guard prevents does not happen.
/// </para>
/// </summary>
public static class TrainingDummyPolicy
{
    /// <summary>Automation that may fight a dummy, by the source name its busy bridge registers.</summary>
    public const string Odysseus = "Odysseus";

    /// <summary>True when the plugin holding the combat override targets dummies deliberately.</summary>
    public static bool AutomationMayFight(string? overrideSource) =>
        string.Equals(overrideSource, Odysseus, StringComparison.Ordinal);
}
