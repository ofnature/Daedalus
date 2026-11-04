using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Config;
using Daedalus.Data;
using Daedalus.Services.Eureka;
using Daedalus.Services.Occult;

namespace Daedalus.Windows.Config.Shared;

/// <summary>
/// Eureka config section: the Magia Board, the elemental level guard, a live readout, and the Logos Actions
/// Daedalus uses, each with its own toggle and a SLOTTED chip read off the duty bar.
/// </summary>
public sealed class EurekaSection
{
    private static readonly Vector4 HeaderColor = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Vector4 Green = new(0.49f, 0.79f, 0.49f, 1f);
    private static readonly Vector4 Amber = new(0.92f, 0.72f, 0.33f, 1f);
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);

    private static readonly string[] MagiaLabels = ["Off (leave it to me)", "Attack", "Defense", "Defense on a tank, Attack otherwise"];

    private static readonly (string Title, LostRole[] Roles, string Help)[] Groups =
    [
        ("Raise", [LostRole.Raise],
            "Raises a dead party member in 30y. Two toons never start on the same body. Sacrifice L has ten uses, "
            + "so it is off unless you turn it on."),
        ("Heals", [LostRole.SingleHeal, LostRole.AreaHeal],
            "Fire on party members below the heal threshold; Cure L III needs 2 or more. Cure L II (a weave) goes first."),
        ("Stances", [LostRole.Stance],
            "Put up whenever missing, in or out of combat. Only one Wisdom can be active, so a second slotted Wisdom "
            + "never replaces the first. Spirit of the Remembered has a single use."),
        ("Party buffs", [LostRole.PartyBuff],
            "Kept on you, then every party member in 30y. Out of combat they are renewed before running low; in "
            + "combat only a missing one is cast."),
        ("Combat", [LostRole.EvasionDown, LostRole.LowHpSelf],
            "Feint L keeps Evasion Down on Wind-aligned enemies (they evade a lot). Bloodbath L below 50% HP."),
    ];

    private readonly Configuration config;
    private readonly Action save;
    private readonly PhantomJobService? dutyState;

    public EurekaSection(Configuration config, Action save, PhantomJobService? dutyState)
    {
        this.config = config;
        this.save = save;
        this.dutyState = dutyState;
    }

    public void Draw()
    {
        var cfg = config.Eureka;
        ImGui.TextColored(HeaderColor, "Eureka");
        ImGui.Separator();
        DrawReadout();
        ImGui.Spacing();

        ImGui.TextColored(HeaderColor, "Magia Board");
        ImGui.Separator();
        var mode = (int)cfg.MagiaMode;
        if (ConfigUIHelpers.StringCombo("Magia Board", ref mode, MagiaLabels,
                "Uses the game's own /magiaauto, so the game picks the element for whatever you target. Attack: the "
                + "element that beats it. Defense: its own element (less damage from it). The last option goes by your job: Defense "
                + "on a tank, Attack on any other job, so each box in a party sets its own. Each turn costs Magia "
                + "Aether, so the setting only changes when it has to (a job change, or entering Eureka).",
                () => { }))
        {
            cfg.MagiaMode = (EurekaMagiaMode)mode;
            save();
        }

        cfg.MaxLevelsAbove = ConfigUIHelpers.IntSlider(
            "Farm: max levels above you", cfg.MaxLevelsAbove, 0, 8,
            "Farm mode skips enemies more than this many elemental levels above you. They hit much harder past +1 "
            + "and give no EXP past +8.",
            save, v => cfg.MaxLevelsAbove = v);

        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, "Logos Actions");
        ImGui.Separator();
        ImGui.TextColored(Dim, "Daedalus only uses Logos Actions you have set to a duty action slot.");
        ConfigUIHelpers.Toggle(
            "Enable Logos Actions",
            () => cfg.Logos.EnableLostActions,
            v => cfg.Logos.EnableLostActions = v,
            "Master toggle for the Logos Action layer.",
            save);
        ConfigUIHelpers.Toggle(
            "Buff the party too",
            () => cfg.Logos.BuffParty,
            v => cfg.Logos.BuffParty = v,
            "Party buffs: on = you first, then every party member in 30y; off = only yourself.",
            save);
        cfg.Logos.HealHpPct = ConfigUIHelpers.FloatSlider(
            "Heal below HP%", cfg.Logos.HealHpPct, 0.10f, 1.00f, "%.2f",
            "Cure L, II and III fire on party members below this.",
            save, v => cfg.Logos.HealHpPct = v);

        var slots = dutyState?.GetDutySlotIds() ?? [];
        foreach (var (title, roles, help) in Groups)
        {
            ImGui.Spacing();
            ImGui.TextColored(HeaderColor, title);
            ImGui.Separator();
            ImGui.TextColored(Dim, help);

            foreach (var def in LogosActionData.All)
            {
                if (Array.IndexOf(roles, def.Role) < 0)
                    continue;

                ConfigUIHelpers.Toggle(
                    def.Name,
                    () => cfg.Logos.IsEnabled(def),
                    v => cfg.Logos.ActionEnabled[def.ActionId] = v,
                    null,
                    save,
                    def.ActionId);
                if (Array.IndexOf(slots, def.ActionId) >= 0)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Green, "[SLOTTED]");
                }
            }
        }
    }

    /// <summary>
    /// The edge dock's Eureka tab: the readout, the Magia Board mode, the Farm level limit and toggles for the slotted
    /// Logos Actions. Tooltips instead of help text, so it stays narrow; the rest is in Settings ▸ Eureka.
    /// </summary>
    public void DrawDock()
    {
        var cfg = config.Eureka;
        var scale = Dalamud.Interface.Utility.ImGuiHelpers.GlobalScale;
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 380f * scale);
        DrawReadout();
        ImGui.PopTextWrapPos();
        ImGui.Separator();

        var mode = (int)cfg.MagiaMode;
        ImGui.SetNextItemWidth(230f * scale);
        if (ImGui.Combo("Magia Board##eurekaDockMagia", ref mode, MagiaLabels, MagiaLabels.Length))
        {
            cfg.MagiaMode = (EurekaMagiaMode)mode;
            save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Uses the game's own /magiaauto: Attack = the element that beats your target, Defense = its own\n"
                             + "element. The last option goes by your job: Defense on a tank, Attack on any other job.\n"
                             + "Each turn costs Magia Aether, so it only changes when it has to.");

        var above = cfg.MaxLevelsAbove;
        ImGui.SetNextItemWidth(230f * scale);
        if (ImGui.SliderInt("Farm: max levels above##eurekaDockLevels", ref above, 0, 8))
        {
            cfg.MaxLevelsAbove = above;
            save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Farm mode skips enemies more than this many elemental levels above you.");

        ImGui.Separator();
        var logosOn = cfg.Logos.EnableLostActions;
        if (ImGui.Checkbox("Logos Actions##eurekaDockLogos", ref logosOn))
        {
            cfg.Logos.EnableLostActions = logosOn;
            save();
        }

        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Use the Logos Actions in your duty slots. Heal threshold and party buffs: Settings ▸ Eureka.");

        var slots = dutyState?.GetDutySlotIds() ?? [];
        var any = false;
        foreach (var def in LogosActionData.All)
        {
            if (Array.IndexOf(slots, def.ActionId) < 0)
                continue;
            any = true;
            var on = cfg.Logos.IsEnabled(def);
            ImGui.Indent();
            if (ImGui.Checkbox($"{def.Name}##eurekaDock{def.ActionId}", ref on))
            {
                cfg.Logos.ActionEnabled[def.ActionId] = on;
                save();
            }

            ImGui.Unindent();
        }

        if (!any)
            ImGui.TextColored(Dim, "No Logos Actions slotted.");
    }

    /// <summary>Your elemental level, the board, and your target's element and level — live, in Eureka.</summary>
    private static void DrawReadout()
    {
        var eureka = EurekaService.Current;
        if (eureka is not { InEureka: true })
        {
            ImGui.TextColored(Dim, "Not in Eureka. The readout shows here once you are.");
            return;
        }

        var player = eureka.Player;
        var me = EurekaService.Foray(player);
        if (eureka.Board() is { } board)
        {
            var placed = string.Join(" ", ElementsWith(board.Placed));
            ImGui.TextColored(Dim,
                $"Elemental level {me?.Level.ToString() ?? "?"} · EXP {board.Exp}/{board.ExpNeeded} · Magia Aether {board.MagiaAether} · "
                + $"magicite {board.Magicite}{(placed.Length > 0 ? $" ({placed})" : "")} · auto {eureka.MagiaAuto ?? "not set"}");
        }

        if (player?.TargetObject is { } target && EurekaService.Foray(target) is { } them)
        {
            var gap = me is { } mine ? them.Level - mine.Level : 0;
            ImGui.TextColored(gap > 1 ? Amber : Dim,
                $"Target {target.Name}: {EurekaRules.ElementName(them.Element)}, level {them.Level}"
                + (gap != 0 ? $" ({gap:+0;-0} on you)" : ""));
        }
    }

    private static System.Collections.Generic.IEnumerable<string> ElementsWith(byte[] placed)
    {
        for (var i = 0; i < placed.Length; i++)
        {
            if (placed[i] > 0)
                yield return $"{EurekaRules.ElementName((byte)(i + 1))} {placed[i]}";
        }
    }
}
