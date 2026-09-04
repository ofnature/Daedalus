using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using Daedalus.Data;
using Daedalus.Services.Occult;

namespace Daedalus.Windows;

/// <summary>
/// Compact Occult Crescent zone HUD. Auto-opens on entering an Occult zone (config
/// toggle), closes on leaving. Shows knowledge level, currencies, phantom
/// consumables, and a banner when a locked purchasable phantom job's soul shard
/// is affordable with the current silver/gold.
/// </summary>
public sealed class OccultWindow : Window
{
    private static readonly Vector4 Gold = new(0.83f, 0.69f, 0.35f, 1f);
    private static readonly Vector4 Green = new(0.49f, 0.79f, 0.49f, 1f);
    private static readonly Vector4 Dim = new(0.54f, 0.54f, 0.58f, 1f);

    /// <summary>Sealing / about to start — no longer joinable, but not finished either.</summary>
    private static readonly Vector4 Warn = new(0.85f, 0.75f, 0.10f, 1f);

    private readonly PhantomJobService _phantomJobs;
    private readonly Daedalus.Services.Occult.PotFateTracker? _potFates;
    private readonly Daedalus.Services.Occult.PhantomBuffCycleService? _buffCycle;
    private readonly Daedalus.Config.PhantomConfig? _phantomConfig;

    public OccultWindow(
        PhantomJobService phantomJobs,
        Daedalus.Services.Occult.PotFateTracker? potFates = null,
        Daedalus.Services.Occult.PhantomBuffCycleService? buffCycle = null,
        Daedalus.Config.PhantomConfig? phantomConfig = null)
        : base("Occult Crescent##DaedalusOccultHud",
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        _potFates = potFates;
        _phantomJobs = phantomJobs;
        _buffCycle = buffCycle;
        _phantomConfig = phantomConfig;
    }

    public override void Draw()
    {
        var snapshot = _phantomJobs.GetSnapshot();
        if (!snapshot.InOccultCrescent)
        {
            ImGui.TextColored(Dim, "Not in Occult Crescent.");
            return;
        }

        if (snapshot.ActiveJob != PhantomJob.None)
            ImGui.TextColored(Green, $"Phantom {PhantomJobData.GetJobDisplayName(snapshot.ActiveJob)}  Lv.{snapshot.Level}");
        else
            ImGui.TextColored(Dim, "No phantom job equipped");

        if (snapshot.Progression is { } prog)
        {
            ImGui.Text($"Knowledge Lv.{prog.KnowledgeLevel}");
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"{prog.KnowledgeExp:N0} / {prog.KnowledgeExpNeeded:N0}");
            ImGui.TextColored(Gold, $"Silver {prog.Silver:N0}");
            ImGui.SameLine();
            ImGui.TextColored(Gold, $"  Gold {prog.Gold:N0}");
        }

        ImGui.Separator();

        foreach (var item in snapshot.Items)
        {
            if (item.Count == 0)
                ImGui.TextColored(Dim, $"{item.Name}: 0");
            else
                ImGui.Text($"{item.Name}: {item.Count:N0}");
        }

        DrawShardEncounters(snapshot);

        DrawCriticalEncounters(snapshot);

        DrawBuffCycle(snapshot);

        DrawPotFates();


        // Affordable-shard banner: locked purchasable jobs the player can buy right now in
        // THIS zone. Both exchanges are cataloged now, and the lookup is zone-scoped so an
        // Obol balance is never measured against South Horn's Pieces prices (or vice versa).
        if (snapshot.Progression is { } p && snapshot.JobLevels.Count > 0)
        {
            var affordable = PhantomJobData.GetAffordableLockedShards(snapshot.JobLevels, p.Silver, p.Gold, snapshot.TerritoryId);
            if (affordable.Count > 0)
            {
                ImGui.Separator();
                foreach (var (job, kind, price) in affordable)
                {
                    var unit = snapshot.TerritoryId == PhantomJobData.NorthHornTerritoryId ? "obols" : "pieces";
                    var currency = kind == PhantomJobData.UnlockKind.SilverShard ? $"silver {unit}" : $"gold {unit}";
                    ImGui.TextColored(Gold, $"★ You can afford Phantom {PhantomJobData.GetJobDisplayName(job)} — {price:N0} {currency}");
                }

                ImGui.TextColored(Dim, snapshot.TerritoryId == PhantomJobData.NorthHornTerritoryId
                    ? "North Horn Currency Exchange"
                    : "Expedition Antiquarian (X:38.1 Y:7.0)");
            }
        }
    }

    /// <summary>
    /// Magic-pot FATE watch. A pot pays ~160 Silver AND ~160 Gold Obols — one is worth 30-50
    /// trash kills — and they run on a ~30 minute cycle alternating between the two spawns, so
    /// the only real cost is not being there. Estimates are labelled as such until a second
    /// spawn of the same FATE lets the tracker measure the real gap.
    /// </summary>
    /// <summary>
    /// Live Critical Encounters that drop a Soul Shard for a job this character has NOT
    /// unlocked. These four jobs have no other unlock route, and the encounters are on their own
    /// timers, so missing one costs a whole job. Goes quiet the moment the job is unlocked —
    /// after that the encounter is just another CE and doesn't need a banner.
    /// </summary>
    private void DrawShardEncounters(Daedalus.Services.Occult.PhantomStateSnapshot snapshot)
    {
        var unclaimed = PhantomJobData.UnclaimedShardEncountersWithStage(
            snapshot.ActiveCriticalEncounters, snapshot.JobLevels);
        if (unclaimed.Count == 0)
            return;

        ImGui.Separator();
        foreach (var (encounter, job) in unclaimed)
        {
            var jobName = PhantomJobData.GetJobDisplayName(job);

            // Joinable and sealed are completely different calls to action, so they must not
            // look alike: one is "drop what you are doing", the other is "you missed it".
            if (encounter.CanJoin)
            {
                var timer = encounter.TimeLeftLabel;
                var head = encounter.ParticipantsLabel;
                var suffix = string.IsNullOrEmpty(timer) ? string.Empty : $" — {timer} to enter";
                if (!string.IsNullOrEmpty(head))
                    suffix += $" ({head})";

                ImGui.TextColored(Gold, $"★★ {encounter.Name} — JOIN NOW{suffix}");
                ImGui.TextColored(Dim, $"drops the {jobName} shard");
            }
            else
            {
                ImGui.TextColored(Dim,
                    $"☆ {encounter.Name} — {encounter.StageLabel}, can't join ({jobName} shard)");
            }
        }

        ImGui.TextColored(Dim, "you don't have this job yet — CE drop is the only way to unlock it");
    }

    /// <summary>
    /// Every live Critical Encounter with the stage it is in. "A CE is up" on its own is not
    /// actionable — Register means the entry timer is running and you can still make it, Warmup
    /// and Battle mean the arena has sealed.
    /// </summary>
    private void DrawCriticalEncounters(Daedalus.Services.Occult.PhantomStateSnapshot snapshot)
    {
        var encounters = snapshot.ActiveCriticalEncounters;
        if (encounters.Count == 0)
            return;

        ImGui.Separator();
        ImGui.TextColored(Dim, "Critical Encounters");

        foreach (var ce in encounters)
        {
            var color = ce.Stage switch
            {
                Daedalus.Services.Occult.CriticalEncounterStage.Register => Gold,
                Daedalus.Services.Occult.CriticalEncounterStage.Warmup => Warn,
                _ => Dim,
            };

            var line = $"{ce.Name} — {ce.StageLabel}";
            if (ce.TimeLeftLabel.Length > 0)
                line += $" {ce.TimeLeftLabel}";
            if (ce.ParticipantsLabel.Length > 0)
                line += $" ({ce.ParticipantsLabel})";
            if (ce.Stage == Daedalus.Services.Occult.CriticalEncounterStage.Battle && ce.Progress > 0)
                line += $" {ce.Progress}%";

            ImGui.TextColored(color, line);
        }
    }

    /// <summary>
    /// The buff cycle: one button, plus enough state that a minute of the character switching
    /// jobs never looks like a hang.
    ///
    /// <para>
    /// Phantom self-buffs last ~30 minutes and survive a job switch, so one pass leaves you
    /// carrying all of them on whatever you actually play — and beside a Knowledge Crystal they
    /// reach the whole party in the zone, so one toon covers a fleet.
    /// </para>
    /// </summary>
    private void DrawBuffCycle(Daedalus.Services.Occult.PhantomStateSnapshot snapshot)
    {
        if (_buffCycle is null || _phantomConfig is null)
            return;

        ImGui.Separator();

        var running = _buffCycle.IsRunning;
        var blocked = _buffCycle.BlockedReason();

        var disabled = blocked.Length > 0;
        if (disabled)
            ImGui.BeginDisabled();

        if (ImGui.Button("Apply phantom buffs"))
        {
            _buffCycle.Start(buff => buff.Job switch
            {
                PhantomJob.Knight => _phantomConfig.BuffCycleKnight,
                PhantomJob.Bard => _phantomConfig.BuffCycleBard,
                PhantomJob.Monk => _phantomConfig.BuffCycleMonk,
                PhantomJob.Dancer => _phantomConfig.BuffCycleDancer,
                _ => false,
            });
        }

        if (disabled)
            ImGui.EndDisabled();

        // Capture hover on the BUTTON, right here. IsItemHovered() reports on the LAST item
        // drawn, and the status text below is drawn next — asking later would test the wrong
        // widget. AllowWhenDisabled keeps the explanation reachable while the button is greyed
        // out, which is exactly when someone wants to know why.
        var buttonHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);

        // A greyed button that will not say why is the thing people report as broken.
        if (blocked.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, blocked);
        }
        else if (!running)
        {
            ImGui.SameLine();
            ImGui.TextColored(Dim, _buffCycle.WouldUseInquiringMind
                ? "one cast — Inquiring Mind covers the whole set"
                : "cycles jobs, then switches you back");
        }

        // Hover only. The previous condition ORed in "enabled and not running", which made
        // SetTooltip fire every frame — ImGui then drew it at the cursor permanently, so the
        // tooltip followed the mouse around the screen instead of appearing on the button.
        if (buttonHovered)
        {
            // Crystal proximity is not required to buff YOURSELF — it is what makes the buffs
            // reach the party across the zone. Saying so stops it reading as a hard requirement.
            var crystalNote = snapshot.InOccultCrescent
                ? "\n\nBeside a Knowledge Crystal these reach every party member in the zone;\n"
                  + "away from one they still land on you alone."
                : string.Empty;
            ImGui.SetTooltip(
                "Switches through the phantom jobs collecting their 30-minute self-buffs,\n"
                + "then returns you to the job you are on now." + crystalNote);
        }

        if (running)
        {
            ImGui.TextColored(Gold, _buffCycle.Status);
        }
        else if (_buffCycle.LastOutcome.Length > 0)
        {
            ImGui.TextColored(Dim, _buffCycle.LastOutcome);
        }

        DrawBuffTimers();
    }

    /// <summary>
    /// Remaining time per collectable buff, so "do I need to re-run this?" is answerable without
    /// opening anything. Buffs the character cannot hold are listed with the reason rather than
    /// shown at zero, which would read as "expired" for something never obtainable.
    /// </summary>
    private void DrawBuffTimers()
    {
        if (_buffCycle is null || _phantomConfig is null)
            return;

        foreach (var buff in Daedalus.Data.PhantomBuffs.All)
        {
            var enabled = buff.Job switch
            {
                PhantomJob.Knight => _phantomConfig.BuffCycleKnight,
                PhantomJob.Bard => _phantomConfig.BuffCycleBard,
                PhantomJob.Monk => _phantomConfig.BuffCycleMonk,
                PhantomJob.Dancer => _phantomConfig.BuffCycleDancer,
                _ => false,
            };

            if (!enabled)
                continue;

            var remaining = _buffCycle.RemainingFor(buff);
            var label = remaining > 0f
                ? $"{(int)remaining / 60}:{(int)remaining % 60:D2}"
                : "—";
            var color = remaining > 600f ? Green : remaining > 0f ? Warn : Dim;

            ImGui.TextColored(color, "●");
            ImGui.SameLine(0f, 4f);
            ImGui.TextColored(Dim, $"{buff.ActionName}");
            ImGui.SameLine();
            ImGui.TextColored(color, label);
        }
    }

    private void DrawPotFates()
    {
        if (_potFates is null)
            return;

        ImGui.Separator();

        // Treasure hunt in progress — the coffer at the end of it is 1,000 Silver + 1,600
        // Gold Obols, so this outranks everything else the window has to say.
        if (_phantomJobs.PlayerHasStatus(Daedalus.Services.Occult.PotFateTracker.TreasureHuntStatusId))
        {
            ImGui.TextColored(Gold, "★★ TREASURE HUNT ACTIVE — use the Magical Elixir to follow it");
            ImGui.TextColored(Dim, "coffer pays 1,000 silver + 1,600 gold; the pot dies to AoEs");
            return;
        }

        if (_potFates.ActiveFate is { } live)
        {
            ImGui.TextColored(Gold,
                $"★ POT FATE UP — {Daedalus.Services.Occult.PotFateTracker.NameWithSpot(live)}");
            if (_potFates.CanOpenMap && ImGui.Button("Show on map##potfate"))
                _potFates.OpenMapToActivePot();
            return;
        }

        if (_potFates.PotImminent)
            ImGui.TextColored(Gold, "★ Pot FATE due within a minute — get there");

        // The alternating cycle means "next pot, either kind" is the actionable number.
        if (_potFates.SecondsUntilNextPot() is { } nextPot)
        {
            if (nextPot <= 0)
                ImGui.TextColored(Green, "Next pot FATE: due now");
            else
                ImGui.TextColored(Gold, $"Next pot FATE: ~{(int)(nextPot / 60):00}:{(int)(nextPot % 60):00}");
        }

        foreach (var name in _potFates.PotFateNames)
        {
            // Spot label matters as much as the name: within a Horn the two spots have produced
            // different coffer tiers, so "north pots" / "south pots" is the actionable part.
            var label = Daedalus.Services.Occult.PotFateTracker.NameWithSpot(name);
            var due = _potFates.SecondsUntilExpected(name);
            if (due is null)
            {
                ImGui.TextColored(Dim, $"{label}: not seen yet");
                continue;
            }

            var tag = _potFates.CycleIsMeasured(name) ? "" : " (est)";
            if (due <= 0)
                ImGui.TextColored(Green, $"{label}: due now{tag}");
            else
            {
                var mins = (int)(due.Value / 60);
                var secs = (int)(due.Value % 60);
                ImGui.TextColored(Dim, $"{label}: ~{mins:00}:{secs:00}{tag}");
            }
        }
    }
}
