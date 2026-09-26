using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Data;
using Daedalus.Services.Occult;

namespace Daedalus.Windows.Config.Shared;

/// <summary>
/// Bozja config section: the Lost Actions Daedalus uses (RSR's set), each with its own toggle and a live
/// SLOTTED chip read off the duty bar.
/// </summary>
public sealed class BozjaSection
{
    private static readonly Vector4 HeaderColor = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Vector4 Green = new(0.49f, 0.79f, 0.49f, 1f);
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);

    private static readonly (string Title, LostRole[] Roles, string Help)[] Groups =
    [
        ("Raise", [LostRole.Raise],
            "Raises a dead party member in 30y. Two toons never start on the same body. Lost Sacrifice KOs "
            + "you 10 seconds later, so it is off unless you turn it on."),
        ("Heals", [LostRole.SingleHeal, LostRole.AreaHeal, LostRole.SelfAreaHeal],
            "Fire on party members below the heal threshold; the area heals need 2 or more. Weave heals go "
            + "first, and a GCD heal is only spent when no weave heal is ready."),
        ("Barriers", [LostRole.Barrier, LostRole.AreaBarrier],
            "In combat. Stoneskin goes on whoever the enemy is casting at; Stoneskin II on everyone near you "
            + "when an AoE is coming (a raidwide on the timeline, or an enemy cast aimed at no one)."),
        ("Party buffs", [LostRole.PartyBuff],
            "Kept on you, then every party member in 30y. Out of combat they are renewed before running "
            + "low; in combat only a missing one is cast. Tier II goes on ahead of tier I and replaces it."),
        ("Forges", [LostRole.Forge],
            "Against an enemy with Magical Aversion, physical attackers get Spellforge; against Physical "
            + "Aversion, healers and casters get Steelsting."),
        ("Damage", [LostRole.AversionAoe, LostRole.AoeDot, LostRole.GapCloser, LostRole.Cone],
            "Lost Seraph Strike is never used by a healer (Cleric Stance cuts healing by 60%) and waits for "
            + "a safe landing."),
        ("Combat self buffs", [LostRole.SelfBuff, LostRole.FreeGcdSelfBuff],
            "Used on cooldown in combat, in weave slots the job leaves. Lost Focus only takes a GCD the job "
            + "left empty. Banner of Solemn Clarity ends the moment you act or move, so it is off unless you "
            + "turn it on."),
    ];

    private readonly Configuration config;
    private readonly Action save;
    private readonly PhantomJobService? dutyState;

    public BozjaSection(Configuration config, Action save, PhantomJobService? dutyState)
    {
        this.config = config;
        this.save = save;
        this.dutyState = dutyState;
    }

    public void Draw()
    {
        ImGui.TextColored(HeaderColor, "Bozja — Lost Actions");
        ImGui.Separator();
        ImGui.TextColored(Dim, "Daedalus only uses Lost Actions you have set to a duty action slot from your holster.");
        ImGui.Spacing();

        ConfigUIHelpers.Toggle(
            "Enable Lost Actions",
            () => config.Bozja.EnableLostActions,
            v => config.Bozja.EnableLostActions = v,
            "Master toggle for the Bozja Lost Action layer.",
            save);

        ConfigUIHelpers.Toggle(
            "Buff the party too",
            () => config.Bozja.BuffParty,
            v => config.Bozja.BuffParty = v,
            "Party buffs and forges: on = you first, then every party member in 30y; off = only yourself.",
            save);

        config.Bozja.HealHpPct = ConfigUIHelpers.FloatSlider(
            "Heal below HP%", config.Bozja.HealHpPct, 0.10f, 1.00f, "%.2f",
            "Lost Cures fire on party members below this.",
            save, v => config.Bozja.HealHpPct = v);

        ConfigUIHelpers.Toggle(
            "Lost Burst / Rampage as AoE damage",
            () => config.Bozja.AversionAoeSpam,
            v => config.Bozja.AversionAoeSpam = v,
            "On (RSR's default): used whenever 2+ enemies are within 10y. Off: only against an enemy with the "
            + "matching aversion that doesn't already have the debuff.",
            save);

        var slots = dutyState?.GetDutySlotIds() ?? [];
        foreach (var (title, roles, help) in Groups)
        {
            ImGui.Spacing();
            ImGui.TextColored(HeaderColor, title);
            ImGui.Separator();
            ImGui.TextColored(Dim, help);

            foreach (var def in BozjaActionData.All)
            {
                if (Array.IndexOf(roles, def.Role) < 0)
                    continue;

                var slotted = Array.IndexOf(slots, def.ActionId) >= 0;
                ConfigUIHelpers.Toggle(
                    def.Name,
                    () => config.Bozja.IsEnabled(def),
                    v => config.Bozja.ActionEnabled[def.ActionId] = v,
                    null,
                    save,
                    def.ActionId);
                if (slotted)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Green, "[SLOTTED]");
                }
            }
        }
    }
}
