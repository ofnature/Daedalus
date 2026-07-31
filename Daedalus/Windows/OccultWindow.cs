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

    private readonly PhantomJobService _phantomJobs;

    public OccultWindow(PhantomJobService phantomJobs)
        : base("Occult Crescent##DaedalusOccultHud",
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing)
    {
        _phantomJobs = phantomJobs;
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
            ImGui.TextColored(Green, $"Phantom {snapshot.ActiveJob}  Lv.{snapshot.Level}");
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
                    ImGui.TextColored(Gold, $"★ You can afford Phantom {job} — {price:N0} {currency}");
                }

                ImGui.TextColored(Dim, snapshot.TerritoryId == PhantomJobData.NorthHornTerritoryId
                    ? "North Horn Currency Exchange"
                    : "Expedition Antiquarian (X:38.1 Y:7.0)");
            }
        }
    }
}
