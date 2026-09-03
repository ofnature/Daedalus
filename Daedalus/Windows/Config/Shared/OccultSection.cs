using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Data;
using Daedalus.Services.Occult;

namespace Daedalus.Windows.Config.Shared;

/// <summary>
/// Occult Crescent config section: phantom-layer master toggles, per-phantom-job
/// option groups, and — for jobs the character has not unlocked — where to get them.
/// Live level/locked chips come from PhantomJobService and only resolve in zone;
/// outside the zone every job renders neutrally with its unlock source for reference.
/// The roster is split South Horn / North Horn (24 jobs in one list buries whichever
/// zone you are actually standing in), and the zone you are in is listed first.
/// </summary>
public sealed class OccultSection
{
    private static readonly Vector4 HeaderColor = new(0.8f, 0.8f, 0.8f, 1f);
    private static readonly Vector4 Green = new(0.49f, 0.79f, 0.49f, 1f);
    private static readonly Vector4 Red = new(0.88f, 0.48f, 0.42f, 1f);
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);

    private readonly Configuration config;
    private readonly Action save;
    private readonly PhantomJobService? phantomJobService;

    public OccultSection(Configuration config, Action save, PhantomJobService? phantomJobService)
    {
        this.config = config;
        this.save = save;
        this.phantomJobService = phantomJobService;
    }

    public void Draw()
    {
        ImGui.TextColored(HeaderColor, "Occult Crescent — Phantom Actions");
        ImGui.Separator();

        var snapshot = phantomJobService?.GetSnapshot();
        var inZone = snapshot?.InOccultCrescent == true;

        var zoneName = inZone ? PhantomJobData.GetZoneName(snapshot!.TerritoryId) : "";
        if (inZone && snapshot!.ActiveJob != PhantomJob.None)
            ImGui.TextColored(Green, $"In {zoneName} — active: Phantom {JobLabel(snapshot.ActiveJob)} Lv.{snapshot.Level}");
        else if (inZone)
            ImGui.TextColored(Green, $"In {zoneName} — no phantom job equipped");
        else
            ImGui.TextColored(Dim, "Not in Occult Crescent — job levels and lock states resolve in zone.");

        ImGui.Spacing();

        ConfigUIHelpers.Toggle(
            "Enable Phantom Actions",
            () => config.Occult.EnablePhantomActions,
            v => config.Occult.EnablePhantomActions = v,
            "Master toggle for the phantom duty-action layer inside Occult Crescent. " +
            "Only actions slotted on the duty bar are ever used; phantom actions never fire " +
            "while a rotation-critical buff is up.",
            save);

        ConfigUIHelpers.Toggle(
            "Save damage actions for burst window",
            () => config.Occult.SaveDamageForBurst,
            v => config.Occult.SaveDamageForBurst = v,
            "Damage phantom actions (cannons, spellblades…) only fire inside the main job's " +
            "burst window. Only applies when burst data exists (raid buffs seen this session) — " +
            "solo farming with no burst windows fires damage actions on cooldown. Heals, " +
            "mitigation, utility and executes ignore this.",
            save);

        ConfigUIHelpers.Toggle(
            "Stand still to land phantom hard casts",
            () => config.Occult.PauseMovementForPhantomCasts,
            v => config.Occult.PauseMovementForPhantomCasts = v,
            "Phantom actions with a cast time are skipped while you are moving — and you count as " +
            "moving the whole time BossMod or vNavmesh is steering you. On a melee job in a " +
            "critical encounter that can silence an entirely hard-cast kit like Phantom Red Mage " +
            "for minutes at a time. With this on, movement pauses briefly so the cast lands, the " +
            "same way it already does for a hardcast raise. It ONLY pauses when BossMod says the " +
            "spot is safe for the whole cast, and it lets go the instant the ground turns " +
            "dangerous — a mechanic always wins over a cast.",
            save);

        ConfigUIHelpers.Toggle(
            "Show zone HUD window",
            () => config.Occult.ShowOccultHud,
            v => config.Occult.ShowOccultHud = v,
            "Compact window that auto-opens in Occult Crescent: knowledge level, silver/gold, " +
            "consumable counts, and a banner when you can afford a locked phantom job's soul shard.",
            save);

        ImGui.Spacing();

        // Split rosters: 24 jobs in one flat list buries whichever zone you're actually in.
        // The zone you're standing in leads.
        var northFirst = inZone && snapshot!.TerritoryId == PhantomJobData.NorthHornTerritoryId;
        if (northFirst)
        {
            DrawRoster("North Horn Jobs", north: true, snapshot, inZone);
            DrawRoster("South Horn Jobs", north: false, snapshot, inZone);
        }
        else
        {
            DrawRoster("South Horn Jobs", north: false, snapshot, inZone);
            DrawRoster("North Horn Jobs", north: true, snapshot, inZone);
        }
    }

    private void DrawRoster(string title, bool north, PhantomStateSnapshot? snapshot, bool inZone)
    {
        ImGui.TextColored(HeaderColor, title);
        ImGui.Separator();

        foreach (var entry in PhantomJobData.LevelStatuses)
        {
            if (PhantomJobData.IsNorthHornJob(entry.Key) == north)
                DrawJobGroup(entry.Key, snapshot, inZone);
        }

        ImGui.Spacing();
    }

    private void DrawJobGroup(PhantomJob job, PhantomStateSnapshot? snapshot, bool inZone)
    {
        byte level = 0;
        var levelKnown = inZone && snapshot != null && snapshot.JobLevels.TryGetValue(job, out level);
        var locked = levelKnown && level == 0;

        var suffix = levelKnown ? (locked ? "locked" : $"Lv.{level}") : "";
        var isActive = snapshot?.ActiveJob == job;
        var header = $"Phantom {JobLabel(job)}{(suffix.Length > 0 ? $" — {suffix}" : "")}{(isActive ? "  [active]" : "")}###occult_{job}";

        if (locked)
            ImGui.PushStyleColor(ImGuiCol.Text, Dim);
        var open = ImGui.CollapsingHeader(header, isActive ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
        if (locked)
            ImGui.PopStyleColor();

        if (!open)
            return;

        ImGui.Indent();

        if (locked)
            ImGui.TextColored(Red, $"Locked — {PhantomJobData.GetUnlockHint(job)}");
        else
            ImGui.TextColored(Dim, $"Unlock: {PhantomJobData.GetUnlockHint(job)}");

        DrawJobOptions(job);
        DrawActionList(job, levelKnown ? level : (byte)0, levelKnown);

        ImGui.Unindent();
        ImGui.Spacing();
    }

    /// <summary>
    /// Where each Phantom Blue Mage spell is learned. This job is the one that cannot be worked
    /// out in game — it levels by learning from enemies rather than by earning experience, and
    /// nothing in the UI says which enemy teaches what.
    /// </summary>
    private void DrawBlueMageSpellSources()
    {
        ImGui.TextColored(Dim,
            "Learned from enemies, not levels: the enemy must USE the spell in front of you, then die.");
        ImGui.TextColored(Dim, "Every source is in North Horn — South Horn teaches this job nothing.");
        ImGui.Spacing();

        var level = phantomJobService?.GetSnapshot()?.ActiveJob == PhantomJob.PhantomBlueMage
            ? phantomJobService.GetSnapshot()?.Level ?? 0
            : (byte)0;

        if (!ImGui.BeginTable("BluSpellSources", 4,
                ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
        {
            return;
        }

        ImGui.TableSetupColumn("Spell");
        ImGui.TableSetupColumn("Lv", ImGuiTableColumnFlags.WidthFixed, 26f);
        ImGui.TableSetupColumn("Learned from");
        ImGui.TableSetupColumn("Where");
        ImGui.TableHeadersRow();

        foreach (var s in PhantomBlueMageSources.All)
        {
            // Slotted on the duty bar is the closest thing to a "you have this" signal we can
            // read — you cannot slot a spell you have not learned.
            var have = phantomJobService?.IsSlotted(s.ActionId) == true;
            var tooEarly = level > 0 && level < s.RequiredLevel;
            var colour = have ? Green : tooEarly ? Dim : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            ImGui.TextColored(colour, have ? $"{s.Spell}  (have)" : s.Spell);

            ImGui.TableNextColumn();
            ImGui.TextColored(colour, s.RequiredLevel.ToString());

            ImGui.TableNextColumn();
            ImGui.TextColored(colour, s.Enemy);

            ImGui.TableNextColumn();
            ImGui.TextColored(colour, s.Where.Length > 0 ? s.Where : "—");
        }

        ImGui.EndTable();

        ImGui.Spacing();
        ImGui.TextColored(Dim,
            "Reference data, not something Daedalus observed — the game files carry no link between "
            + "these spells and the enemies that teach them.");
    }

    private void DrawJobOptions(PhantomJob job)
    {
        switch (job)
        {
            case PhantomJob.Freelancer:
                config.Occult.FreelancerResuscitationHpPct = ConfigUIHelpers.FloatSlider(
                    "Occult Resuscitation below HP%", config.Occult.FreelancerResuscitationHpPct,
                    0.10f, 1.00f, "%.2f", null, save, v => config.Occult.FreelancerResuscitationHpPct = v);
                ConfigUIHelpers.Toggle("Use Occult Treasuresight",
                    () => config.Occult.UseTreasuresight, v => config.Occult.UseTreasuresight = v,
                    null, save);
                break;

            case PhantomJob.Knight:
                config.Occult.KnightHealHpPct = ConfigUIHelpers.ThresholdSliderSmall(
                    "Occult Heal below",
                    config.Occult.KnightHealHpPct, 30f, 100f,
                    "Self-heal with Occult Heal below this. Set high by default because it is " +
                    "nearly free — an instant ability on a 5 second recast, so it costs a weave " +
                    "slot rather than a GCD.",
                    save, v => config.Occult.KnightHealHpPct = v);
                ConfigUIHelpers.Toggle("Use Pray as a heal",
                    () => config.Occult.KnightPrayAsHeal, v => config.Occult.KnightPrayAsHeal = v,
                    "Pray is a weaponskill, so it costs a GCD — off by default. Occult Heal above " +
                    "is the cheap heal and is always on.",
                    save);
                ConfigUIHelpers.Toggle("Use Pledge on yourself",
                    () => config.Occult.KnightPledgeSelf, v => config.Occult.KnightPledgeSelf = v,
                    "Pledge makes you impervious to most attacks for 10 seconds on a 2 minute " +
                    "recast — an emergency button, not a heal. Targeting an ally with it is not " +
                    "supported yet.",
                    save);
                if (config.Occult.KnightPledgeSelf)
                {
                    config.Occult.KnightPledgeHpPct = ConfigUIHelpers.ThresholdSliderSmall(
                        "Pledge below",
                        config.Occult.KnightPledgeHpPct, 10f, 60f,
                        "Fire the invulnerability below this. Kept low deliberately — it is a " +
                        "death-saver on a 2 minute cooldown, so spending it on chip damage wastes it.",
                        save, v => config.Occult.KnightPledgeHpPct = v);
                }
                break;

            case PhantomJob.TimeMage:
                ConfigUIHelpers.Toggle("Keep Slow on the pull",
                    () => config.Occult.TimeMageUseSlowga, v => config.Occult.TimeMageUseSlowga = v,
                    "Occult Slowga does no damage — it hangs Slow +80% for 30 seconds on your " +
                    "target and everything within 5 yalms of it. Costs one GCD per pack, and it " +
                    "is the only action a level 1 Time Mage has.",
                    save);
                break;

            case PhantomJob.Monk:
                config.Occult.MonkKickMaxRangeYalms = ConfigUIHelpers.FloatSlider(
                    "Phantom Kick max range (y)", config.Occult.MonkKickMaxRangeYalms,
                    1f, 15f, "%.1f", "Phantom Kick dashes to the target — long ranges can dash into hazards.",
                    save, v => config.Occult.MonkKickMaxRangeYalms = v);
                config.Occult.MonkChakraMpThreshold = ConfigUIHelpers.IntSlider(
                    "Occult Chakra when MP below", config.Occult.MonkChakraMpThreshold,
                    0, 10000, null, save, v => config.Occult.MonkChakraMpThreshold = v);
                config.Occult.MonkChakraHpPct = ConfigUIHelpers.FloatSlider(
                    "Occult Chakra when HP below", config.Occult.MonkChakraHpPct,
                    0.05f, 1.00f, "%.2f", null, save, v => config.Occult.MonkChakraHpPct = v);
                break;

            case PhantomJob.Chemist:
                ConfigUIHelpers.Toggle("Occult Potion on self only",
                    () => config.Occult.ChemistPotionSelfOnly, v => config.Occult.ChemistPotionSelfOnly = v,
                    null, save);
                config.Occult.ChemistPotionHpPct = ConfigUIHelpers.FloatSlider(
                    "Potion below HP%", config.Occult.ChemistPotionHpPct,
                    0.05f, 1.00f, "%.2f", null, save, v => config.Occult.ChemistPotionHpPct = v);
                ConfigUIHelpers.Toggle("Occult Ether on self only",
                    () => config.Occult.ChemistEtherSelfOnly, v => config.Occult.ChemistEtherSelfOnly = v,
                    null, save);
                config.Occult.ChemistEtherMpThreshold = ConfigUIHelpers.IntSlider(
                    "Ether below MP", config.Occult.ChemistEtherMpThreshold,
                    0, 10000, null, save, v => config.Occult.ChemistEtherMpThreshold = v);
                config.Occult.ChemistElixirPartyHpPct = ConfigUIHelpers.FloatSlider(
                    "Elixir below party avg HP%", config.Occult.ChemistElixirPartyHpPct,
                    0.05f, 1.00f, "%.2f",
                    "Potion and Ether both consume Occult Potion items; Elixir consumes an Occult Elixir.",
                    save, v => config.Occult.ChemistElixirPartyHpPct = v);
                break;

            case PhantomJob.Oracle:
                ConfigUIHelpers.Toggle("Use Phantom Judgment",
                    () => config.Occult.OracleUseJudgment, v => config.Occult.OracleUseJudgment = v, null, save);
                ConfigUIHelpers.Toggle("Use Cleansing",
                    () => config.Occult.OracleUseCleansing, v => config.Occult.OracleUseCleansing = v, null, save);
                ConfigUIHelpers.Toggle("Use Blessing",
                    () => config.Occult.OracleUseBlessing, v => config.Occult.OracleUseBlessing = v, null, save);
                ConfigUIHelpers.Toggle("Use Starfall",
                    () => config.Occult.OracleUseStarfall, v => config.Occult.OracleUseStarfall = v, null, save);
                ConfigUIHelpers.Toggle("Save Invulnerability for Starfall combo",
                    () => config.Occult.OracleSaveInvulnForStarfall, v => config.Occult.OracleSaveInvulnForStarfall = v,
                    "Starfall deals massive damage to you as well — with this on, Invulnerability is held to absorb it.",
                    save);
                config.Occult.OracleJudgmentPartyHpPct = ConfigUIHelpers.FloatSlider(
                    "Predict-to-heal: Judgment below party HP%", config.Occult.OracleJudgmentPartyHpPct,
                    0.10f, 1.00f, "%.2f", null, save, v => config.Occult.OracleJudgmentPartyHpPct = v);
                config.Occult.OracleBlessingPartyHpPct = ConfigUIHelpers.FloatSlider(
                    "Predict-to-heal: Blessing below party HP%", config.Occult.OracleBlessingPartyHpPct,
                    0.10f, 1.00f, "%.2f", null, save, v => config.Occult.OracleBlessingPartyHpPct = v);
                break;

            case PhantomJob.Cannoneer:
                ConfigUIHelpers.Toggle("Prefer Dark Cannon (off = Shock Cannon) when target takes both debuffs",
                    () => config.Occult.CannoneerPreferDarkCannon, v => config.Occult.CannoneerPreferDarkCannon = v,
                    null, save);
                ConfigUIHelpers.Toggle("Fall back to Dark Cannon (off = Shock Cannon) when target is immune to both",
                    () => config.Occult.CannoneerImmuneFallbackDark, v => config.Occult.CannoneerImmuneFallbackDark = v,
                    null, save);
                break;

            case PhantomJob.Geomancer:
                ConfigUIHelpers.Toggle("Use Suspend in combat",
                    () => config.Occult.GeomancerSuspendInCombat, v => config.Occult.GeomancerSuspendInCombat = v, null, save);
                ConfigUIHelpers.Toggle("Use Suspend out of combat",
                    () => config.Occult.GeomancerSuspendOutOfCombat, v => config.Occult.GeomancerSuspendOutOfCombat = v,
                    "Weather buffs (Sunbath, Cloudy Caress, Blessed Rain…) are automatic — the game only offers the one matching current weather.",
                    save);
                break;

            case PhantomJob.PhantomBlueMage:
                DrawBlueMageSpellSources();
                break;

            case PhantomJob.PhantomRedMage:
                config.Occult.RedMageCureHpPct = ConfigUIHelpers.ThresholdSlider(
                    "Occult Cure II below HP%", config.Occult.RedMageCureHpPct, 0.10f, 1f,
                    "40,000 cure potency for 1,500 MP. It is a 1.5s spell so it costs a GCD — set this at a real deficit rather than a top-off. " +
                    "Occult Libra fires automatically at enemies whose weakness isn't known yet; the Fire/Blizzard/Thunder II trio share one 30s recast and pick by weakness.",
                    save);
                ConfigUIHelpers.Toggle("Lead the matched nuke with Occult Cure II",
                    () => config.Occult.RedMagePrimeDualcastWithCure,
                    v => config.Occult.RedMagePrimeDualcastWithCure = v,
                    "Needs the level 6 Dualcast trait. Casting the heal earns Dualcast, so the " +
                    "weakness-matched nuke goes out INSTANTLY next GCD — no pausing to cast and " +
                    "nothing can interrupt it. Only runs when the target's element is already known.",
                    save);
                if (config.Occult.RedMagePrimeDualcastWithCure)
                {
                    config.Occult.RedMagePrimeMpFloor = ConfigUIHelpers.IntSlider(
                        "Stop priming below MP", config.Occult.RedMagePrimeMpFloor,
                        0, 10000,
                        "Cure II is 1,500 MP a go, so this is why priming can work all fight and " +
                        "then quietly stop — the bar drained under the floor. Lower it to keep " +
                        "priming longer, raise it to keep more in reserve for raises (~2,400 each). " +
                        "The Duty tab says when this is what stopped it.",
                        save, v => config.Occult.RedMagePrimeMpFloor = v);
                }
                break;

            case PhantomJob.Necromancer:
                ImGui.TextColored(Common.DaedalusTheme.StatusRed,
                    "Deep Freeze DOOMS you for 10s — you die unless healed to FULL HP in time.");
                ConfigUIHelpers.Toggle("Use the Doom nukes (dangerous — needs a healer)",
                    () => config.Occult.NecromancerUseDeepFreeze, v => config.Occult.NecromancerUseDeepFreeze = v,
                    "Covers ALL FOUR: Deep Freeze, Hell Wind, Chaos Drive and Doomsday. Every one of them costs 10% of max HP " +
                    "and applies Doom to yourself, and the Doom is dispelled ONLY by a heal back to 100%. " +
                    "Leave this off for solo or unattended play.",
                    save);
                if (config.Occult.NecromancerUseDeepFreeze)
                {
                    ConfigUIHelpers.Toggle("Require the Drain Touch buff first (recommended)",
                        () => config.Occult.NecromancerDeepFreezeRequireDrainTouch,
                        v => config.Occult.NecromancerDeepFreezeRequireDrainTouch = v,
                        "Drain Touch stops most attacks dropping you below 1 HP — it makes the HP cost survivable and raises Deep Freeze's potency (300→400, 390→520 on ice-weak targets).",
                        save);
                    ConfigUIHelpers.Toggle("Match the target's elemental weakness",
                        () => config.Occult.NecromancerMatchElementalWeakness,
                        v => config.Occult.NecromancerMatchElementalWeakness = v,
                        "Deep Freeze (ice), Hell Wind (wind) and Chaos Drive (lightning) share one 40s recast — they are one nuke in three elements. " +
                        "Fires whichever matches the target's revealed weakness: 520 potency instead of 400 (+120 a cast). Unknown weakness falls back to Deep Freeze.",
                        save);
                    ConfigUIHelpers.Toggle("Use Doomsday (120s, strips a buff)",
                        () => config.Occult.NecromancerUseDoomsday,
                        v => config.Occult.NecromancerUseDoomsday = v,
                        "Unaspected 350 potency (500 under Drain Touch) on its own 120s recast, and it removes one beneficial status from the target. " +
                        "Dooms you exactly like the elemental nukes, so the same healer requirement applies.",
                        save);
                    config.Occult.NecromancerDeepFreezeMinHpPercent = ConfigUIHelpers.ThresholdSlider(
                        "Minimum HP to cast Deep Freeze",
                        config.Occult.NecromancerDeepFreezeMinHpPercent, 0.5f, 1f,
                        "Below this, Deep Freeze is held — the 10% cost plus incoming damage must leave a hole a healer can climb you out of.",
                        save);
                }

                break;

            default:
                ImGui.TextColored(Dim, "No options — fully automated.");
                break;
        }
    }

    private static void DrawActionList(PhantomJob job, byte currentLevel, bool levelKnown)
    {
        ImGui.Spacing();
        foreach (var def in PhantomActions.ForJob(job))
        {
            var reachable = !levelKnown || def.RequiredLevel <= currentLevel;
            var procTag = def.RequiresProc ? " (proc)" : "";
            if (reachable)
                ImGui.TextColored(Dim, $"Lv.{def.RequiredLevel}  {def.Name}{procTag}");
            else
                ImGui.TextColored(Red, $"Lv.{def.RequiredLevel}  {def.Name}{procTag} — not yet learned");
        }
    }

    private static string JobLabel(PhantomJob job) => PhantomJobData.GetJobDisplayName(job);
}
