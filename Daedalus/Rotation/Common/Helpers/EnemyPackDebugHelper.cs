using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Daedalus.Rotation.Common;
using Daedalus.Services.Targeting;
using System.Numerics;

namespace Daedalus.Rotation.Common.Helpers;

/// <summary>
/// Shared engaged + AoE-range enemy counting for rotation modules and debug UI.
/// </summary>
public static class EnemyPackDebugHelper
{
    public static EnemyPackCounts Count(ITargetingService targeting, float aoeRadiusYalms, IPlayerCharacter player)
        => targeting.CountEnemyPack(aoeRadiusYalms, player);

    public static void Apply(IEnemyPackDebug debug, in EnemyPackCounts counts)
    {
        debug.EngagedEnemies = counts.Engaged;
        debug.AoeRangeEnemies = counts.AoeRange;
    }

    public static void SyncAoEDps(BaseDebugState common, IEnemyPackDebug job, int aoeMin, float aoeRadiusYalms)
    {
        common.AoEDpsEnemyCount = job.AoeRangeEnemies;
        common.AoEDpsEngagedCount = job.EngagedEnemies;
        common.AoEDpsRadiusYalms = aoeRadiusYalms;
        common.AoEDpsState = FormatAoEDpsState(job.AoeRangeEnemies, job.EngagedEnemies, aoeMin, aoeRadiusYalms);
    }

    /// <summary>Draws engaged + in-AoE rows inside an existing ImGui table.</summary>
    public static void DrawEnemyPackTableRows(IEnemyPackDebug state, float aoeRadiusYalms)
    {
        DrawCountRow("Engaged:", state.EngagedEnemies, engagedHighlightMin: 2);
        DrawCountRow($"In AoE ({FormatRadius(aoeRadiusYalms)}):", state.AoeRangeEnemies, engagedHighlightMin: 3);
    }

    public static string FormatAoEDpsState(int inAoeRange, int engaged, int aoeMin, float aoeRadiusYalms)
    {
        var radius = FormatRadius(aoeRadiusYalms);
        if (inAoeRange >= aoeMin)
            return $"AoE ({inAoeRange} in {radius})";
        if (inAoeRange > 0 || engaged > 0)
            return $"ST ({inAoeRange} in {radius})";
        return "No enemies";
    }

    public static string FormatWhyStuckAoE(string aoEDpsState, int engaged, int inAoeRange, float aoeRadiusYalms)
    {
        if (engaged > 0 || inAoeRange > 0)
            return $"{aoEDpsState} ({engaged} engaged, {inAoeRange} in {FormatRadius(aoeRadiusYalms)})";
        return aoEDpsState;
    }

    public static void DrawEnemyPackRows(IEnemyPackDebug state, float aoeRadiusYalms, float firstColumnWidth = 140f)
    {
        if (ImGui.BeginTable("EnemyPackCounts", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, firstColumnWidth);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            DrawEnemyPackTableRows(state, aoeRadiusYalms);
            ImGui.EndTable();
        }
    }

    private static void DrawCountRow(string label, int count, int engagedHighlightMin)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Text(label);
        ImGui.TableNextColumn();
        var color = count >= engagedHighlightMin
            ? new Vector4(1f, 0.6f, 0.2f, 1f)
            : new Vector4(0.7f, 0.7f, 0.7f, 1f);
        ImGui.TextColored(color, count.ToString());
    }

    private static string FormatRadius(float yalms)
        => yalms % 1f < 0.05f ? $"{yalms:0}y" : $"{yalms:0.#}y";

    public static string FormatRadiusLabel(float yalms) => FormatRadius(yalms);
}
