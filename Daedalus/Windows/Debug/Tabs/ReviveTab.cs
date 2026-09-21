using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Daedalus.Data;
using Daedalus.Services.Diagnostics;
using Daedalus.Services.Party;

namespace Daedalus.Windows.Debug.Tabs;

/// <summary>
/// Every way Daedalus can put a body back on its feet, on one screen, with the reason each one is or
/// is not doing it.
///
/// <para>
/// Built 2026-09-20 after "no reviving is getting done from healers, phoenix downs, nor variants".
/// Each path already worked out a precise verdict every frame and then dropped it somewhere different
/// — a job tab, a line nobody read, or nowhere at all — so answering that question meant reading four
/// subsystems' source, which produced four wrong guesses before the cause turned up. Anything that can
/// raise reports to <see cref="ReviveDiagnostics"/>; this reads it.
/// </para>
/// </summary>
public static class ReviveTab
{
    private static readonly Vector4 Good = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 Held = new(0.90f, 0.75f, 0.35f, 1f);
    private static readonly Vector4 Dim = new(0.65f, 0.65f, 0.65f, 1f);
    private static readonly Vector4 Bad = new(0.90f, 0.45f, 0.45f, 1f);

    /// <summary>The pending-resurrection status on a corpse somebody has already raised.</summary>
    private const uint RaisePendingStatusId = 148;

    public static void Draw(
        IObjectTable? objectTable,
        IPartyList? partyList,
        IPartyCoordinationService? partyCoordination)
    {
        ImGui.TextColored(Dim,
            "Every revive path reports here. A path that is silent has not run at all this session.");
        ImGui.Spacing();

        DrawSources();
        ImGui.Spacing();
        DrawBodies(objectTable, partyList, partyCoordination);
        ImGui.Spacing();
        DrawReservations(partyCoordination);
    }

    private static void DrawSources()
    {
        ImGui.Text("Revive sources");
        ImGui.Separator();

        foreach (var source in new[]
                 {
                     ReviveSource.HealerRaise, ReviveSource.PhoenixDown,
                     ReviveSource.VariantRaise, ReviveSource.PhantomRaise,
                 })
        {
            var label = Label(source);
            var entry = ReviveDiagnostics.For(source);

            if (entry is not { } e)
            {
                ImGui.TextColored(Dim, $"{label,-16} never reported");
                continue;
            }

            ImGui.TextColored(Colour(e.State), $"{label,-16} {e.State}");
            ImGui.SameLine();
            ImGui.TextColored(Dim, $"  ({e.AgeSeconds:F0}s ago)");
        }
    }

    /// <summary>
    /// Who is actually down, and the things every raise path checks before it will touch them: range,
    /// and whether a raise is already pending. A body that fails these is invisible to all of them.
    /// </summary>
    private static void DrawBodies(
        IObjectTable? objectTable, IPartyList? partyList, IPartyCoordinationService? partyCoordination)
    {
        ImGui.Text("Bodies");
        ImGui.Separator();

        if (partyList is null || partyList.Length == 0)
        {
            ImGui.TextColored(Dim, "no party — solo, or the party list is not readable from here");
            return;
        }

        var self = objectTable?.LocalPlayer;
        var any = false;

        foreach (var member in partyList)
        {
            if (member?.GameObject is not IBattleChara chara || !chara.IsDead)
                continue;

            any = true;
            var name = chara.Name?.TextValue ?? "?";
            var job = member.ClassJob.RowId;
            var role = JobRegistry.IsHealer(job) ? "healer" : JobRegistry.IsTank(job) ? "tank" : "dps";
            var distance = self is null ? -1f : Vector3.Distance(self.Position, chara.Position);
            var pending = HasStatus(chara, RaisePendingStatusId);
            var reserved = partyCoordination?.IsRaiseTargetReservedByOther((uint)chara.GameObjectId) == true;

            var note = pending ? "raise already pending — every path skips it"
                : reserved ? "reserved by another toon"
                : distance < 0 ? "distance unknown"
                : distance > 30f ? $"{distance:F0}y — out of raise range (30y)"
                : distance > 15f ? $"{distance:F0}y — in raise range, too far for Phoenix Down (15y)"
                : $"{distance:F0}y — in range of everything";

            ImGui.TextColored(pending || reserved ? Dim : distance > 30f ? Bad : Good,
                $"  {name} ({role}) — {note}");
        }

        if (!any)
            ImGui.TextColored(Dim, "nobody down");
    }

    private static void DrawReservations(IPartyCoordinationService? partyCoordination)
    {
        ImGui.Text("Raise reservations (shared between toons)");
        ImGui.Separator();

        var reservations = partyCoordination?.GetRemoteRaiseReservations();
        if (reservations is null || reservations.Count == 0)
        {
            ImGui.TextColored(Dim, "none — nothing is being raised by another instance");
            return;
        }

        foreach (var (targetId, reservation) in reservations)
            ImGui.TextColored(Held, $"  target {targetId} — {(reservation.IsExpired ? "expired" : "held")}");
    }

    private static string Label(ReviveSource source) => source switch
    {
        ReviveSource.HealerRaise => "Healer raise",
        ReviveSource.PhoenixDown => "Phoenix Down",
        ReviveSource.VariantRaise => "Variant Raise",
        ReviveSource.PhantomRaise => "Phantom raise",
        _ => source.ToString(),
    };

    /// <summary>Green for doing something, amber for a deliberate hold, grey for nothing to do.</summary>
    private static Vector4 Colour(string state)
    {
        if (state.Contains("casting", System.StringComparison.OrdinalIgnoreCase)
            || state.Contains("queued", System.StringComparison.OrdinalIgnoreCase)
            || state.Contains("Dead member found", System.StringComparison.OrdinalIgnoreCase))
            return Good;

        if (state.Contains("none", System.StringComparison.OrdinalIgnoreCase)
            || state.Contains("nobody", System.StringComparison.OrdinalIgnoreCase)
            || state.Contains("no target", System.StringComparison.OrdinalIgnoreCase))
            return Dim;

        return Held;
    }

    private static bool HasStatus(IBattleChara chara, uint statusId)
    {
        if (chara.StatusList == null)
            return false;

        foreach (var status in chara.StatusList)
        {
            if (status != null && status.StatusId == statusId)
                return true;
        }

        return false;
    }
}
