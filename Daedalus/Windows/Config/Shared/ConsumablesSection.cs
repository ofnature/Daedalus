using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Localization;

namespace Daedalus.Windows.Config.Shared;

/// <summary>
/// Renders the Consumables config section: master toggle for tincture
/// automation plus the empty-inventory warning toggle.
/// </summary>
public sealed class ConsumablesSection
{
    private readonly Configuration config;
    private readonly Action save;

    public ConsumablesSection(Configuration config, Action save)
    {
        this.config = config;
        this.save = save;
    }

    public void Draw()
    {
        ImGui.TextColored(new Vector4(0.8f, 0.8f, 0.8f, 1f),
            Loc.T(LocalizedStrings.Consumables.ConsumablesHeader, "Consumables"));
        ImGui.Separator();

        ConfigUIHelpers.Toggle(
            Loc.T(LocalizedStrings.Consumables.EnableAutoTincture, "Auto-use combat tinctures"),
            () => config.Consumables.EnableAutoTincture,
            v => config.Consumables.EnableAutoTincture = v,
            Loc.T(LocalizedStrings.Consumables.EnableAutoTinctureDesc,
                "When enabled, Daedalus will use a combat tincture during opener and re-pot windows. Only fires in high-end content (savage, extreme, ultimate, criterion, chaotic alliance). Default off because pots cost real gil."),
            save);

        ImGui.Spacing();

        ConfigUIHelpers.Toggle(
            "Allow outside high-end content",
            () => config.Consumables.UseOutsideHighEnd,
            v => config.Consumables.UseOutsideHighEnd = v,
            "Also pot in normal duties and legacy content (unsynced Coil clears, farm runs). Off = the classic savage/extreme/ultimate-only behavior. Pots still only fire in burst windows, on the countdown, or at pull intent.",
            save);

        ImGui.Spacing();

        ConfigUIHelpers.Toggle(
            "Auto Phoenix Down when all healers are down",
            () => config.Consumables.EnablePhoenixDown,
            v => config.Consumables.EnablePhoenixDown = v,
            "When every healer in the party is dead, hardcasts a Phoenix Down (8s cast, 15y) on the nearest dead healer. Works in 4-player duties, deep dungeons, and field operations like the Occult Crescent — the game blocks it in 8-player trials and raids. Tanks hold it unless they are the last one alive. With LAN coordination only one toon casts per corpse. Phoenix Downs are 1,000 gil at gil vendors.",
            save);

        ImGui.Spacing();

        ConfigUIHelpers.Toggle(
            Loc.T(LocalizedStrings.Consumables.WarnOnEmptyInventory, "Warn when inventory is empty"),
            () => config.Consumables.WarnOnEmptyInventory,
            v => config.Consumables.WarnOnEmptyInventory = v,
            Loc.T(LocalizedStrings.Consumables.WarnOnEmptyInventoryDesc,
                "Fires a one-shot chat warning per fight when auto-tincture is enabled but no matching tincture is in your inventory."),
            save);
    }
}
