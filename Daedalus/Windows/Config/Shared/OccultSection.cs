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

        if (inZone && snapshot!.ActiveJob != PhantomJob.None)
            ImGui.TextColored(Green, $"In South Horn — active: Phantom {snapshot.ActiveJob} Lv.{snapshot.Level}");
        else if (inZone)
            ImGui.TextColored(Green, "In South Horn — no phantom job equipped");
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
            "Show zone HUD window",
            () => config.Occult.ShowOccultHud,
            v => config.Occult.ShowOccultHud = v,
            "Compact window that auto-opens in Occult Crescent: knowledge level, silver/gold, " +
            "consumable counts, and a banner when you can afford a locked phantom job's soul shard.",
            save);

        ImGui.Spacing();
        ImGui.TextColored(HeaderColor, "Phantom Jobs");
        ImGui.Separator();

        foreach (var entry in PhantomJobData.LevelStatuses)
            DrawJobGroup(entry.Key, snapshot, inZone);
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
                ConfigUIHelpers.Toggle("Use Pray as a heal",
                    () => config.Occult.KnightPrayAsHeal, v => config.Occult.KnightPrayAsHeal = v,
                    null, save);
                ConfigUIHelpers.Toggle("Pledge on self (off = most-attacked ally)",
                    () => config.Occult.KnightPledgeSelf, v => config.Occult.KnightPledgeSelf = v,
                    null, save);
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

            case PhantomJob.Necromancer:
                ImGui.TextColored(Common.DaedalusTheme.StatusRed,
                    "Deep Freeze DOOMS you for 10s — you die unless healed to FULL HP in time.");
                ConfigUIHelpers.Toggle("Use Deep Freeze (dangerous — needs a healer)",
                    () => config.Occult.NecromancerUseDeepFreeze, v => config.Occult.NecromancerUseDeepFreeze = v,
                    "Costs 10% of max HP and applies Doom to yourself. The Doom is dispelled ONLY by a heal back to 100%. " +
                    "Leave this off for solo or unattended play.",
                    save);
                if (config.Occult.NecromancerUseDeepFreeze)
                {
                    ConfigUIHelpers.Toggle("Require the Drain Touch buff first (recommended)",
                        () => config.Occult.NecromancerDeepFreezeRequireDrainTouch,
                        v => config.Occult.NecromancerDeepFreezeRequireDrainTouch = v,
                        "Drain Touch stops most attacks dropping you below 1 HP — it makes the HP cost survivable and raises Deep Freeze's potency (300→400, 390→520 on ice-weak targets).",
                        save);
                    ConfigUIHelpers.Toggle("Only on ice-weak targets (once learned)",
                        () => config.Occult.NecromancerDeepFreezePreferIceWeak,
                        v => config.Occult.NecromancerDeepFreezePreferIceWeak = v,
                        "Ice-weak targets take 520 potency instead of 400 (+120 per cast) — about one phantom GCD back every 4 casts. " +
                        "Enemies whose weakness hasn't been revealed yet are still allowed, so this never blocks the action outright.",
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

    private static string JobLabel(PhantomJob job) => job switch
    {
        PhantomJob.TimeMage => "Time Mage",
        PhantomJob.MysticKnight => "Mystic Knight",
        _ => job.ToString(),
    };
}
