using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using Daedalus.Config.DPS;
using Daedalus.Data;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Rotation.ProteusCore.Helpers;

namespace Daedalus.Windows;

/// <summary>
/// BLU Mimicry control — manual role buttons instead of (or alongside) auto-mimicry. Each button
/// scans the AREA for a player of that role and asks the rotation to cast Aetheric Mimicry on
/// them (a fresh cast on a different role OVERWRITES the buff — that's the supported way to
/// change it). Remove uses the targetless-cast trick (user-discovered: mimicry cast with no
/// target strips the buff because it cannot target self). Auto-opens when switching to BLU.
/// </summary>
public sealed class BluMimicryWindow : Window
{
    private static readonly Vector4 Green = new(0.4f, 1.0f, 0.4f, 1.0f);
    private static readonly Vector4 Yellow = new(1.0f, 0.85f, 0.3f, 1.0f);
    private static readonly Vector4 Dim = new(0.6f, 0.6f, 0.6f, 1.0f);

    private readonly IObjectTable objectTable;
    private readonly IPartyList partyList;
    private readonly Func<bool> removeMimicry;
    private readonly Configuration configuration;
    private readonly Action save;
    private readonly CasterPartyHelper partyHelper;

    private string feedback = "";

    // Final Sting calculator simulation state (session-local; baseline persists via config).
    private bool simWaxing;
    private bool simHarmonized;
    private bool simOffGuard;
    private bool simBasicInstinct = true;
    private bool simMightyGuard = true;
    private int simTargetHp;
    private float simSafety = 0.75f;

    public BluMimicryWindow(
        IObjectTable objectTable, IPartyList partyList, Func<bool> removeMimicry,
        Configuration configuration, Action save)
        : base("BLU Mimicry", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.objectTable = objectTable;
        this.partyList = partyList;
        this.removeMimicry = removeMimicry;
        this.configuration = configuration;
        this.save = save;
        this.partyHelper = new CasterPartyHelper(objectTable, partyList);
    }

    public override void Draw()
    {
        var player = objectTable.LocalPlayer;
        if (player == null || player.ClassJob.RowId != JobRegistry.BlueMage)
        {
            ImGui.TextColored(Dim, "Switch to Blue Mage to use mimicry controls.");
            return;
        }

        // Current buff.
        var current =
            BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryTank) ? "Tank"
            : BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryDps) ? "DPS"
            : BaseStatusHelper.HasStatus(player, BLUActions.StatusIds.AethericMimicryHealer) ? "Healer"
            : null;
        ImGui.Text("Current mimicry:");
        ImGui.SameLine();
        if (current != null) ImGui.TextColored(Green, current);
        else ImGui.TextColored(Dim, "none");

        var pending = BluMimicryCommand.GetPending();
        if (pending != null)
            ImGui.TextColored(Yellow, $"Casting: mimic {pending}…");

        ImGui.Separator();
        ImGui.TextColored(Dim, "Scan the area and mimic that role (overwrites the current buff):");

        DrawRoleButton(BluRole.Tank, player);
        DrawRoleButton(BluRole.Dps, player);
        DrawRoleButton(BluRole.Healer, player);

        ImGui.Separator();

        var hasBuff = current != null;
        if (!hasBuff) ImGui.BeginDisabled();
        if (ImGui.Button("Remove mimicry"))
        {
            BluMimicryCommand.Clear();
            BluMimicryCommand.SuppressAuto(15);
            feedback = removeMimicry()
                ? "Removal cast sent — the buff should drop now"
                : "Removal cast refused — try standing still, out of combat";
        }
        if (!hasBuff) ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextColored(Dim, "(targetless recast — auto-mimicry pauses 15s)");

        if (feedback.Length > 0)
            ImGui.TextColored(Yellow, feedback);

        ImGui.Separator();
        DrawFinalStingCalculator();
        // The death-immunity ledger view lives in the RAID window (per-duty aware): current
        // duty's Weak/Immune verdicts inline + the full list grouped by zone.
    }

    /// <summary>
    /// Final Sting damage calculator: calibrate from one observed non-crit hit, then simulate
    /// buff combinations. Shows the NON-CRIT FLOOR on purpose — sting planning wants a
    /// guaranteed kill, and crits only help. Also the seed for the future fleet-sting count.
    /// </summary>
    private void DrawFinalStingCalculator()
    {
        if (!ImGui.CollapsingHeader("Final Sting Calculator"))
            return;

        ImGui.TextColored(Dim, "Calibrate: one observed NON-CRIT, unbuffed hit of the chosen spell.");

        var baselinePotency = configuration.BlueMage.FinalStingBaselinePotency;
        var isSonicBoom = Math.Abs(baselinePotency - 210f) < 1f;
        if (ImGui.RadioButton("Sonic Boom (210p)", isSonicBoom))
        {
            configuration.BlueMage.FinalStingBaselinePotency = 210f;
            save();
        }
        ImGui.SameLine();
        if (ImGui.RadioButton("Final Sting test (2000p)", !isSonicBoom))
        {
            configuration.BlueMage.FinalStingBaselinePotency = 2000f;
            save();
        }

        var baseline = configuration.BlueMage.FinalStingBaselineDamage;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("Observed damage", ref baseline))
        {
            configuration.BlueMage.FinalStingBaselineDamage = Math.Max(0, baseline);
            save();
        }

        ImGui.Spacing();
        ImGui.TextColored(Dim, "Simulate buffs:");
        ImGui.Checkbox("Moon Flute (×1.5)", ref simWaxing);
        ImGui.SameLine();
        ImGui.Checkbox("Whistle (×1.8)", ref simHarmonized);
        ImGui.Checkbox("Off-guard (×1.05)", ref simOffGuard);
        ImGui.SameLine();
        ImGui.Checkbox("Basic Instinct (×2)", ref simBasicInstinct);
        ImGui.Checkbox("Mighty Guard (×0.6)", ref simMightyGuard);
        ImGui.SameLine();
        ImGui.TextColored(Dim, "Bristle does NOT apply — the sting is physical.");

        var estimate = Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.Estimate(
            configuration.BlueMage.FinalStingBaselineDamage,
            configuration.BlueMage.FinalStingBaselinePotency,
            simWaxing, simHarmonized, simOffGuard, simBasicInstinct, simMightyGuard);

        ImGui.Spacing();
        if (estimate <= 0f)
        {
            ImGui.TextColored(Dim, "Enter an observed damage number to calibrate.");
            return;
        }

        ImGui.Text("Estimated Final Sting:");
        ImGui.SameLine();
        ImGui.TextColored(Green, $"{estimate:N0}");
        ImGui.SameLine();
        ImGui.TextColored(Dim, "(non-crit floor)");

        ImGui.SetNextItemWidth(140);
        if (ImGui.InputInt("Target HP", ref simTargetHp))
            simTargetHp = Math.Max(0, simTargetHp);
        ImGui.SetNextItemWidth(140);
        ImGui.SliderFloat("Safety factor", ref simSafety, 0.5f, 1.0f, "%.2f");

        if (simTargetHp > 0)
        {
            var needed = Daedalus.Rotation.ProteusCore.Helpers.FinalStingCalculator.StingersNeeded(
                simTargetHp, estimate, simSafety);
            ImGui.Text("Stingers needed:");
            ImGui.SameLine();
            ImGui.TextColored(Yellow, needed.ToString());
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"(each credited {estimate * simSafety:N0})");
        }
    }

    private void DrawRoleButton(BluRole role, Dalamud.Game.ClientState.Objects.SubKinds.IPlayerCharacter player)
    {
        var source = MimicryScanHelper.FindArchetypeAlly(partyHelper, partyList, objectTable, player, role, null);
        var found = source != null;

        if (!found) ImGui.BeginDisabled();
        if (ImGui.Button($"Mimic {role}###BluMimic{role}", new Vector2(110, 0)))
        {
            BluMimicryCommand.ClearSuppression();
            BluMimicryCommand.Request(role);
            feedback = "";
        }
        if (!found) ImGui.EndDisabled();

        ImGui.SameLine();
        if (found)
            ImGui.TextColored(Dim, $"from {source!.Name?.TextValue ?? "ally"}");
        else
            ImGui.TextColored(Dim, "none in range");
    }
}
