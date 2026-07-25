using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Data;
using Daedalus.Services.Occult;

namespace Daedalus.Windows.Config.Shared;

/// <summary>
/// Variant dungeon config section: per-action toggles/thresholds, which roles can select
/// each action in the V&amp;C Dungeon Finder, and — inside a variant territory — a live
/// SELECTED chip per action (the instance grants a Set status for each of the two picks).
/// </summary>
public sealed class VariantSection
{
    private static readonly Vector4 HeaderColor = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Vector4 Green = new(0.49f, 0.79f, 0.49f, 1f);
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);

    private readonly Configuration config;
    private readonly Action save;
    private readonly PhantomJobService? dutyState;

    public VariantSection(Configuration config, Action save, PhantomJobService? dutyState)
    {
        this.config = config;
        this.save = save;
        this.dutyState = dutyState;
    }

    public void Draw()
    {
        ImGui.TextColored(HeaderColor, "Variant Dungeons — Duty Actions");
        ImGui.Separator();

        var inZone = dutyState?.IsInVariantDungeon == true;
        if (inZone)
            ImGui.TextColored(Green, "In a Variant/Criterion dungeon — SELECTED chips are live.");
        else
            ImGui.TextColored(Dim, "Not in a Variant dungeon. Actions are chosen in the V&C Dungeon Finder " +
                "(\"Set Actions\", pick two) before entering — Daedalus can only use what was selected and slotted.");

        ImGui.Spacing();

        ConfigUIHelpers.Toggle(
            "Enable Variant Actions",
            () => config.Variant.EnableVariantActions,
            v => config.Variant.EnableVariantActions = v,
            "Master toggle for the variant duty-action layer. Only actions the instance granted " +
            "(your two Dungeon Finder picks) and slotted on the duty bar are ever used.",
            save);

        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, "Actions");
        ImGui.Separator();

        foreach (var def in VariantActionData.All)
            DrawActionGroup(def, inZone);
    }

    private void DrawActionGroup(VariantActionDef def, bool inZone)
    {
        var selected = inZone && dutyState != null && dutyState.PlayerHasStatus(def.SetStatusId);
        var header = $"{def.Name}{(selected ? "  [SELECTED]" : "")}###variant_{def.Kind}";

        var open = ImGui.CollapsingHeader(header, selected ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        if (!open)
            return;

        ImGui.Indent();
        ImGui.TextColored(Dim, $"Selectable by: {def.SelectableBy}   ({(def.IsGcd ? "GCD" : "weave")})");

        switch (def.Kind)
        {
            case VariantAction.Cure:
                config.Variant.CureHpPct = ConfigUIHelpers.FloatSlider(
                    "Cure below HP%", config.Variant.CureHpPct, 0.10f, 1.00f, "%.2f",
                    "Instant 14,000-potency heal + regen; the regen doubles the NEXT Cure. Fires ahead of the job's filler GCD.",
                    save, v => config.Variant.CureHpPct = v);
                break;

            case VariantAction.SpiritDart:
                ConfigUIHelpers.Toggle("Maintain the Spirit Dart DoT",
                    () => config.Variant.UseSpiritDart, v => config.Variant.UseSpiritDart = v,
                    "AoE DoT (30s, 5y spread). Applied when the current target lacks Sustained Damage, " +
                    "then held until it expires — never spammed on its 2.5s recast.",
                    save);
                break;

            case VariantAction.EagleEyeShot:
                ConfigUIHelpers.Toggle("Use Eagle Eye Shot on cooldown",
                    () => config.Variant.UseEagleEyeShot, v => config.Variant.UseEagleEyeShot = v,
                    "60s-recast ranged hit; potency scales with item level.",
                    save);
                break;

            case VariantAction.Rampart:
                ConfigUIHelpers.Toggle("Keep Rampart up in combat",
                    () => config.Variant.UseRampart, v => config.Variant.UseRampart = v,
                    "20% damage reduction for 60s + absorb shield, on a 15s recast — coverage can be permanent.",
                    save);
                ConfigUIHelpers.Toggle("Recast on cooldown (even while the buff is up)",
                    () => config.Variant.RampartSpamOnCooldown, v => config.Variant.RampartSpamOnCooldown = v,
                    "Refreshes the absorb shield every 15s at the cost of extra weave slots.",
                    save);
                break;

            case VariantAction.Raise:
            case VariantAction.RaiseII:
                ConfigUIHelpers.Toggle("Raise dead party members",
                    () => config.Variant.UseRaise, v => config.Variant.UseRaise = v,
                    "8-second hard cast on its own recast timer — held while moving. Raise II is the Criterion version.",
                    save);
                break;

            case VariantAction.Ultimatum:
                ConfigUIHelpers.Toggle("Use Ultimatum (AoE provoke + stun)",
                    () => config.Variant.UseUltimatum, v => config.Variant.UseUltimatum = v,
                    "Places you at the top of nearby enemies' enmity lists. Default OFF — in a multibox " +
                    "party only the main tank should run this.",
                    save);
                break;
        }

        ImGui.Unindent();
        ImGui.Spacing();
    }
}
