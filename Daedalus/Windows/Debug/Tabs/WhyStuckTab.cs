using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Data;
using Daedalus.Localization;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Services.Debug;

namespace Daedalus.Windows.Debug.Tabs;

/// <summary>
/// Why Stuck tab: shows all decision states to diagnose why rotation isn't casting.
/// </summary>
public static class WhyStuckTab
{
    public static void Draw(DebugSnapshot snapshot, Configuration config)
    {
        var rotation = snapshot.Rotation;
        var healing = snapshot.Healing;
        var gcd = snapshot.GcdState;
        var tank = snapshot.Tank;
        var jobId = snapshot.ActiveJobId;
        var isHealer = JobRegistry.IsHealer(jobId);
        var isTank = tank != null;

        DrawCurrentState(rotation, gcd, tank);
        ImGui.Spacing();

        if (IsSectionVisible(config, "GcdPriority"))
        {
            if (isTank)
                DrawTankGcdPriorityChain(tank!);
            else if (isHealer)
                DrawHealerGcdPriorityChain(rotation, healing);
            else
                DrawDpsGcdPriorityChain(rotation);
            ImGui.Spacing();
        }

        if (IsSectionVisible(config, "OgcdState"))
        {
            if (isTank)
                DrawTankOgcdState(tank!, gcd);
            else if (isHealer)
                DrawHealerOgcdState(rotation, gcd);
            else
                DrawGenericOgcdState(rotation, gcd);
            ImGui.Spacing();
        }

        if (IsSectionVisible(config, "DpsDetails"))
        {
            if (isTank)
                DrawTankDamageDetails(tank!);
            else
                DrawDpsDetails(rotation, isHealer);
            ImGui.Spacing();
        }

        if (IsSectionVisible(config, "Resources"))
        {
            if (isTank)
                DrawTankResources(tank!);
            else if (isHealer)
                DrawHealerResources(rotation);
        }
    }

    private static void DrawCurrentState(DebugRotationState rotation, DebugGcdState gcd, DebugTankState? tank)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.CurrentState, "Current State"));
        ImGui.Separator();

        // Loud banner when the whole rotation is globally paused — turns a mystery stall into a stated
        // cause. Computed for every class (DebugService), so this works on all jobs, not just tanks.
        var pauseReason = !string.IsNullOrEmpty(rotation.PauseReason) ? rotation.PauseReason : tank?.PauseReason;
        if (!string.IsNullOrEmpty(pauseReason))
        {
            ImGui.TextColored(DebugColors.Failure, $"PAUSED: {pauseReason}");
        }

        var gcdReady = gcd.CanExecuteGcd;
        var gcdColor = gcdReady ? DebugColors.Success : DebugColors.Warning;
        var gcdText = gcdReady
            ? Loc.T(LocalizedStrings.Debug.GcdReady, "GCD READY")
            : Loc.TFormat(LocalizedStrings.Debug.GcdStateFormat, "GCD: {0} ({1}s)", gcd.State, gcd.GcdRemaining.ToString("F2"));
        ImGui.TextColored(gcdColor, gcdText);

        ImGui.SameLine();
        ImGui.TextColored(DebugColors.Dim, " | ");
        ImGui.SameLine();

        var planColor = DebugColors.GetPlanningStateColor(rotation.PlanningState);
        ImGui.TextColored(planColor, Loc.TFormat(LocalizedStrings.Debug.PriorityFormat, "Priority: {0}", rotation.PlanningState));

        var plannedAction = tank?.PlannedAction ?? rotation.PlannedAction;
        var actionColor = plannedAction != "None" ? DebugColors.Success : DebugColors.Dim;
        ImGui.TextColored(actionColor, Loc.TFormat(LocalizedStrings.Debug.LastFormat, "Last: {0}", plannedAction));

        var distanceColor = rotation.TargetDistanceInfo switch
        {
            "None" => DebugColors.Dim,
            var d when d.Contains("in melee", StringComparison.Ordinal) => DebugColors.Success,
            _ => DebugColors.Warning
        };
        ImGui.TextColored(
            distanceColor,
            Loc.TFormat(LocalizedStrings.Debug.TargetDistanceFormat, "Distance: {0}", rotation.TargetDistanceInfo));
    }

    private static void DrawTankGcdPriorityChain(DebugTankState tank)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.GcdPriorityChainHeader, "GCD Priority Chain (checked top to bottom)"));
        ImGui.Separator();

        if (ImGui.BeginTable("GcdPriorityTable", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Priority, "Priority"), ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Category, "Category"), ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.State, "State"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Target, "Target"), ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableHeadersRow();

            DrawPriorityRow("1", Loc.T(LocalizedStrings.Debug.EnmityStateLabel, "Enmity"), tank.EnmityState, tank.CurrentTarget);
            DrawPriorityRow("2", Loc.T(LocalizedStrings.Debug.MitigationStateLabel, "Mitigation"), tank.MitigationState, "");
            DrawPriorityRow("3", Loc.T(LocalizedStrings.Debug.BuffStateLabel, "Buff"), tank.BuffState, "");
            DrawPriorityRow("4", Loc.T(LocalizedStrings.Debug.DamageStateLabel, "Damage"), tank.DamageState, tank.CurrentTarget);

            ImGui.EndTable();
        }
    }

    private static void DrawHealerGcdPriorityChain(DebugRotationState rotation, DebugHealingState healing)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.GcdPriorityChainHeader, "GCD Priority Chain (checked top to bottom)"));
        ImGui.Separator();

        if (ImGui.BeginTable("GcdPriorityTable", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Priority, "Priority"), ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Category, "Category"), ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.State, "State"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Target, "Target"), ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableHeadersRow();

            DrawPriorityRow("0.5", Loc.T(LocalizedStrings.Debug.Esuna, "Esuna"), rotation.EsunaState, rotation.EsunaTarget);
            DrawPriorityRow("1", Loc.T(LocalizedStrings.Debug.Raise, "Raise"), rotation.RaiseState, rotation.RaiseTarget);

            var aoEDetails = healing.AoEInjuredCount > 0
                ? Loc.TFormat(LocalizedStrings.Debug.InjuredCountFormat, "{0} injured", healing.AoEInjuredCount)
                : "";
            DrawPriorityRow("2", Loc.T(LocalizedStrings.Debug.AoEHeal, "AoE Heal"), healing.AoEStatus, aoEDetails);

            var singleHealState = rotation.PlanningState == "Single Heal"
                ? Loc.T(LocalizedStrings.Debug.ActiveLabel, "Active")
                : Loc.T(LocalizedStrings.Debug.CheckingLabel, "Checking...");
            DrawPriorityRow("3", Loc.T(LocalizedStrings.Debug.SingleHeal, "Single Heal"), singleHealState, "");

            var regenState = rotation.PlanningState == "Regen"
                ? Loc.T(LocalizedStrings.Debug.ActiveLabel, "Active")
                : Loc.T(LocalizedStrings.Debug.CheckingLabel, "Checking...");
            DrawPriorityRow("4", Loc.T(LocalizedStrings.Debug.Regen, "Regen"), regenState, "");

            DrawPriorityRow("5", Loc.T(LocalizedStrings.Debug.DpsLabel, "DPS"), rotation.DpsState, rotation.TargetInfo);

            ImGui.EndTable();
        }
    }

    private static void DrawDpsGcdPriorityChain(DebugRotationState rotation)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.GcdPriorityChainHeader, "GCD Priority Chain (checked top to bottom)"));
        ImGui.Separator();

        if (ImGui.BeginTable("GcdPriorityTable", 4, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Priority, "Priority"), ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Category, "Category"), ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.State, "State"), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.Target, "Target"), ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableHeadersRow();

            DrawPriorityRow("1", Loc.T(LocalizedStrings.Debug.DpsLabel, "DPS"), rotation.DpsState, rotation.TargetInfo);

            ImGui.EndTable();
        }
    }

    private static void DrawPriorityRow(string priority, string category, string state, string target)
    {
        ImGui.TableNextRow();

        ImGui.TableNextColumn();
        ImGui.TextColored(DebugColors.Dim, priority);

        ImGui.TableNextColumn();
        ImGui.Text(category);

        ImGui.TableNextColumn();
        var stateColor = GetStateColor(state);
        ImGui.TextColored(stateColor, state);

        ImGui.TableNextColumn();
        if (!string.IsNullOrEmpty(target) && target != "None")
        {
            ImGui.TextColored(DebugColors.Dim, target);
        }
    }

    private static void DrawTankOgcdState(DebugTankState tank, DebugGcdState gcd)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.OgcdState, "oGCD State"));
        ImGui.Separator();

        var weaveColor = gcd.CanExecuteOgcd ? DebugColors.Success : DebugColors.Dim;
        var weaveText = gcd.CanExecuteOgcd
            ? Loc.TFormat(LocalizedStrings.Debug.WeaveWindowOpen, "Weave Window Open ({0} slots)", gcd.WeaveSlots)
            : Loc.T(LocalizedStrings.Debug.NoWeaveWindow, "No Weave Window");
        ImGui.TextColored(weaveColor, weaveText);

        if (ImGui.BeginTable("OgcdTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.OgcdHeader, "oGCD"), ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.State, "State"), ImGuiTableColumnFlags.WidthStretch);

            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.EnmityStateLabel, "Enmity"), tank.EnmityState);
            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.MitigationStateLabel, "Mitigation"), tank.MitigationState);
            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.BuffStateLabel, "Buff"), tank.BuffState);

            ImGui.EndTable();
        }
    }

    private static void DrawHealerOgcdState(DebugRotationState rotation, DebugGcdState gcd)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.OgcdState, "oGCD State"));
        ImGui.Separator();

        var weaveColor = gcd.CanExecuteOgcd ? DebugColors.Success : DebugColors.Dim;
        var weaveText = gcd.CanExecuteOgcd
            ? Loc.TFormat(LocalizedStrings.Debug.WeaveWindowOpen, "Weave Window Open ({0} slots)", gcd.WeaveSlots)
            : Loc.T(LocalizedStrings.Debug.NoWeaveWindow, "No Weave Window");
        ImGui.TextColored(weaveColor, weaveText);

        if (ImGui.BeginTable("OgcdTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.OgcdHeader, "oGCD"), ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.State, "State"), ImGuiTableColumnFlags.WidthStretch);

            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.ThinAir, "Thin Air"), rotation.ThinAirState);
            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.Asylum, "Asylum"), rotation.AsylumState + (rotation.AsylumTarget != "None" ? $" -> {rotation.AsylumTarget}" : ""));
            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.Temperance, "Temperance"), rotation.TemperanceState);
            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.Defensives, "Defensives"), rotation.DefensiveState);
            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.Surecast, "Surecast"), rotation.SurecastState);

            ImGui.EndTable();
        }
    }

    private static void DrawGenericOgcdState(DebugRotationState rotation, DebugGcdState gcd)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.OgcdState, "oGCD State"));
        ImGui.Separator();

        var weaveColor = gcd.CanExecuteOgcd ? DebugColors.Success : DebugColors.Dim;
        var weaveText = gcd.CanExecuteOgcd
            ? Loc.TFormat(LocalizedStrings.Debug.WeaveWindowOpen, "Weave Window Open ({0} slots)", gcd.WeaveSlots)
            : Loc.T(LocalizedStrings.Debug.NoWeaveWindow, "No Weave Window");
        ImGui.TextColored(weaveColor, weaveText);

        if (ImGui.BeginTable("OgcdTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.OgcdHeader, "oGCD"), ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.State, "State"), ImGuiTableColumnFlags.WidthStretch);

            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.Defensives, "Defensives"), rotation.DefensiveState);
            DrawOgcdRow(Loc.T(LocalizedStrings.Debug.Surecast, "Surecast"), rotation.SurecastState);

            ImGui.EndTable();
        }
    }

    private static void DrawOgcdRow(string name, string state)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(name);
        ImGui.TableNextColumn();
        var color = GetStateColor(state);
        ImGui.TextColored(color, state);
    }

    private static void DrawTankDamageDetails(DebugTankState tank)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.DpsDetails, "DPS Details"));
        ImGui.Separator();

        if (ImGui.BeginTable("DpsTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.TypeHeader, "Type"), ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.State, "State"), ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.DamageStateLabel, "Damage"));
            ImGui.TableNextColumn();
            ImGui.TextColored(GetStateColor(tank.DamageState), tank.DamageState);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.ComboState, "Combo Step"));
            ImGui.TableNextColumn();
            var comboText = tank.ComboStep > 0
                ? $"{tank.ComboStep} ({tank.ComboTimeRemaining:F1}s)"
                : "—";
            ImGui.Text(comboText);

            // Enemy counts that drive the AoE-vs-ST decision: aggroed pull size (25y) and how many the
            // self-centered PBAoE (5y) actually hits. AoE triggers when the 5y count meets the threshold.
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Enemies");
            ImGui.TableNextColumn();
            var pbaoeColor = tank.InAoERange >= 2 ? DebugColors.Success : DebugColors.Warning;
            ImGui.TextColored(pbaoeColor, $"{tank.InAoERange} in 5y");
            ImGui.SameLine();
            ImGui.TextColored(DebugColors.Dim, $" / {tank.AggroedInRange} aggroed in 25y");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.IsMainTankLabel, "Role"));
            ImGui.TableNextColumn();
            var roleText = tank.IsMainTank
                ? Loc.T(LocalizedStrings.Debug.MainTankValue, "Main Tank")
                : Loc.T(LocalizedStrings.Debug.OffTankValue, "Off Tank");
            ImGui.Text(roleText);

            ImGui.EndTable();
        }
    }

    private static void DrawDpsDetails(DebugRotationState rotation, bool isHealer)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.DpsDetails, "DPS Details"));
        ImGui.Separator();

        if (ImGui.BeginTable("DpsTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.TypeHeader, "Type"), ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn(Loc.T(LocalizedStrings.Debug.State, "State"), ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.SingleTarget, "Single Target"));
            ImGui.TableNextColumn();
            ImGui.TextColored(GetStateColor(rotation.DpsState), rotation.DpsState);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.AoE, "AoE"));
            ImGui.TableNextColumn();
            var aoeRadius = rotation.AoEDpsRadiusYalms > 0f ? rotation.AoEDpsRadiusYalms : JobAoERadiusYalms.Melee;
            var aoEText = EnemyPackDebugHelper.FormatWhyStuckAoE(
                rotation.AoEDpsState, rotation.AoEDpsEngagedCount, rotation.AoEDpsEnemyCount, aoeRadius);
            ImGui.TextColored(GetStateColor(rotation.AoEDpsState), aoEText);

            if (isHealer)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(Loc.T(LocalizedStrings.Debug.AfflatusMisery, "Afflatus Misery"));
                ImGui.TableNextColumn();
                ImGui.TextColored(GetStateColor(rotation.MiseryState), rotation.MiseryState);
            }

            ImGui.EndTable();
        }
    }

    private static void DrawTankResources(DebugTankState tank)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.Gauge, "Gauge"));
        ImGui.Separator();

        ImGui.Text($"{tank.GaugeLabel}: {tank.GaugeValue}/{tank.GaugeMax}");
        ImGui.ProgressBar(tank.GaugeMax > 0 ? (float)tank.GaugeValue / tank.GaugeMax : 0f, new Vector2(-1, 0));

        if (tank.ResourceLines.Count == 0)
        {
            ImGui.TextColored(DebugColors.Dim, "No active procs or buffs");
            return;
        }

        ImGui.Spacing();
        if (ImGui.BeginTable("TankResourcesTable", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Resource", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);

            foreach (var (label, value) in tank.ResourceLines)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(label);
                ImGui.TableNextColumn();
                ImGui.TextColored(DebugColors.Success, value);
            }

            ImGui.EndTable();
        }
    }

    private static void DrawHealerResources(DebugRotationState rotation)
    {
        ImGui.Text(Loc.T(LocalizedStrings.Debug.WhmResources, "WHM Resources"));
        ImGui.Separator();

        var lilyColor = rotation.LilyCount > 0 ? DebugColors.Success : DebugColors.Dim;
        ImGui.TextColored(lilyColor, Loc.TFormat(LocalizedStrings.Debug.LilyFormat, "Lily: {0}/3", rotation.LilyCount));
        if (rotation.LilyCount > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(DebugColors.Dim, Loc.T(LocalizedStrings.Debug.SolaceRaptureAvailable, " (Solace/Rapture available)"));
        }

        var bloodLilyColor = rotation.BloodLilyCount >= 3 ? DebugColors.Success : DebugColors.Dim;
        ImGui.TextColored(bloodLilyColor, Loc.TFormat(LocalizedStrings.Debug.BloodLilyFormat, "Blood Lily: {0}/3", rotation.BloodLilyCount));
        if (rotation.BloodLilyCount >= 3)
        {
            ImGui.SameLine();
            ImGui.TextColored(DebugColors.Success, Loc.T(LocalizedStrings.Debug.MiseryReady, " (Misery ready!)"));
        }

        ImGui.TextColored(DebugColors.Dim, Loc.TFormat(LocalizedStrings.Debug.LilyStrategyFormat, "Lily Strategy: {0}", rotation.LilyStrategy));

        if (rotation.SacredSightStacks > 0)
        {
            ImGui.TextColored(DebugColors.Success, Loc.TFormat(LocalizedStrings.Debug.SacredSightFormat, "Sacred Sight: {0} stacks (instant Glare IV)", rotation.SacredSightStacks));
        }
        else
        {
            ImGui.TextColored(DebugColors.Dim, Loc.T(LocalizedStrings.Debug.SacredSightZero, "Sacred Sight: 0 stacks"));
        }
    }

    private static Vector4 GetStateColor(string state)
    {
        if (string.IsNullOrEmpty(state))
            return DebugColors.Dim;

        if (state.StartsWith("Executing") ||
            state.StartsWith("Active") ||
            state == "Ready" ||
            state.StartsWith("Swiftcast") ||
            state.StartsWith("Hardcast") ||
            state.StartsWith("Cleansing") ||
            state.Contains("Glare") ||
            state.Contains("Damage:") ||
            state.Contains("DoT") ||
            state.StartsWith("Main tank") ||
            state.StartsWith("Combo") ||
            state.StartsWith("AoE") ||
            state.StartsWith("Confiteor") ||
            state.StartsWith("Atonement") ||
            state.StartsWith("Holy"))
            return DebugColors.Success;

        if (state.StartsWith("Waiting") ||
            state.StartsWith("CD:") ||
            state.StartsWith("On cooldown") ||
            state.StartsWith("Provoke cooldown") ||
            state.StartsWith("MP ") ||
            state.StartsWith("Level ") ||
            state.StartsWith("Holding") ||
            state.StartsWith("Monitoring") ||
            state.Contains("< min") ||
            state.Contains("/3 Blood") ||
            state.StartsWith("Position "))
            return DebugColors.Warning;

        if (state == "Disabled" ||
            state == "No target" ||
            state == "No enemy found" ||
            state.StartsWith("Healing disabled") ||
            state.StartsWith("Manual mode") ||
            state == "Moving" ||
            state == "Not in combat" ||
            state == "Paused (no target)")
            return DebugColors.Failure;

        return DebugColors.Dim;
    }

    private static bool IsSectionVisible(Configuration config, string section)
    {
        if (config.Debug.DebugSectionVisibility.TryGetValue(section, out var visible))
            return visible;
        return true;
    }
}
