using System.Numerics;
using Dalamud.Bindings.ImGui;
using Daedalus.Localization;
using Daedalus.Rotation.HermesCore.Context;
using Daedalus.Rotation.HermesCore.Helpers;
using Daedalus.Rotation.Common;
using Daedalus.Rotation.Common.Helpers;
using Daedalus.Windows.Debug;

namespace Daedalus.Windows.Debug.Tabs;

/// <summary>
/// Ninja tab: Hermes-specific debug info including mudra, Ninki, and buff tracking.
/// </summary>
public static class HermesTab
{
    public static void Draw(HermesDebugState? state, Configuration config)
    {
        if (state == null)
        {
            ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T(LocalizedStrings.Debug.NinjaNotActive, "Ninja rotation not active."));
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.SwitchToNinja, "Switch to Ninja to see debug info."));
            return;
        }

        DrawDowntimeProbeSection(state);
        ImGui.Spacing();

        // Mudra Section
        DrawMudraSection(state);
        ImGui.Spacing();

        DrawModuleStates(state);
        ImGui.Spacing();

        DrawNinjutsuGateSection(state);
        ImGui.Spacing();

        // Gauge Section
        DrawGaugeSection(state);
        ImGui.Spacing();

        // Buffs Section
        DrawBuffSection(state);
        ImGui.Spacing();

        // Debuffs Section
        DrawDebuffSection(state);
        ImGui.Spacing();

        // Positional Section
        DrawPositionalSection(state);
    }

    private static void DrawDowntimeProbeSection(HermesDebugState state)
    {
        DebugTabHelpers.DrawSection("Downtime Probe", "NinDowntimeProbeTable", () =>
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Live status:");
            ImGui.TableNextColumn();
            if (state.IsTrueIdleDowntime)
            {
                ImGui.TextColored(new Vector4(1f, 0.45f, 0.45f, 1f), "TRUE IDLE (counts as unexplained)");
            }
            else if (state.IsMudraReservationWindow)
            {
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.75f, 1f),
                    $"MUDRA RESERVATION ({state.DowntimeReservationReason})");
            }
            else if (state.IsGcdReadyRaw)
            {
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.4f, 1f), "GCD ready (other category)");
            }
            else
            {
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), "On GCD / busy");
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("CanExecuteGcd (raw):");
            ImGui.TableNextColumn();
            DrawProcStatus(state.IsGcdReadyRaw);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Mudra reservation:");
            ImGui.TableNextColumn();
            DrawProcStatus(state.IsMudraReservationWindow);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Session mudra reservation:");
            ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.75f, 1f),
                $"{state.SessionMudraReservationSeconds:F1}s (excluded from Fight Summary)");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Session true idle:");
            ImGui.TableNextColumn();
            var idleColor = state.SessionTrueIdleSeconds > 5f
                ? new Vector4(1f, 0.45f, 0.45f, 1f)
                : new Vector4(0.9f, 0.9f, 0.9f, 1f);
            ImGui.TextColored(idleColor,
                $"{state.SessionTrueIdleSeconds:F1}s (maps to unexplained downtime)");
        }, 170f);
    }

    private static void DrawMudraSection(HermesDebugState state)
    {
        DebugTabHelpers.DrawSection(Loc.T(LocalizedStrings.Debug.Mudra, "Mudra"), "NinMudraTable", () =>
        {
            // Game mudra status (496)
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Game Mudra (496):");
            ImGui.TableNextColumn();
            DrawProcStatus(state.HasGameMudraStatus);

            // Helper sequence (execute-wait does not block GCD/oGCD)
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Helper Sequence:");
            ImGui.TableNextColumn();
            DrawProcStatus(state.IsMudraSequenceActive);

            // Ten charges (live every frame)
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Ten charges:");
            ImGui.TableNextColumn();
            var tenColor = state.MudraTenIsPressable
                ? new Vector4(0.5f, 1f, 0.5f, 1f)
                : new Vector4(1f, 0.75f, 0.4f, 1f);
            ImGui.TextColored(tenColor,
                $"{state.MudraTenCharges}/{state.MudraTenMaxCharges} | " +
                $"next +{state.MudraTenCooldownRemaining:F1}s | " +
                $"pressable in {state.MudraTenSecondsUntilPressable:F1}s " +
                $"(~{TenMudraChargeTracker.KnownChargeRecastSeconds:F0}s/charge)");

            // Combined (debug)
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.MudraActive, "Mudra Active:"));
            ImGui.TableNextColumn();
            if (state.IsMudraActive)
            {
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), Loc.TFormat(LocalizedStrings.Debug.InSequenceFormat, "Yes ({0} in sequence)", state.MudraCount));
            }
            else
            {
                ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.No, "No"));
            }

            // Mudra Sequence
            if (state.IsMudraActive && !string.IsNullOrEmpty(state.MudraSequence))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(Loc.T(LocalizedStrings.Debug.Sequence, "Sequence:"));
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(1f, 0.8f, 0.5f, 1f), state.MudraSequence);
            }

            // Pending Ninjutsu
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.PendingNinjutsu, "Pending Ninjutsu:"));
            ImGui.TableNextColumn();
            var ninjutsuColor = state.PendingNinjutsu != 0 ? new Vector4(0.5f, 1f, 0.5f, 1f) : new Vector4(0.7f, 0.7f, 0.7f, 1f);
            ImGui.TextColored(ninjutsuColor, state.PendingNinjutsu.ToString());

            // Live AdjustId(Ninjutsu) slot probe
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Ninjutsu Slot ID:");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(
                $"{state.NinjutsuSlotAdjustedId} ({state.NinjutsuSlotProbe}) | AM={state.NinjutsuSlotFromActionManager}");
            if (state.NinjutsuSlotBeforeTenPress != 0 || state.NinjutsuSlotAfterTenPress != 0)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Ten press slot:");
                ImGui.TableNextColumn();
                ImGui.TextWrapped(
                    $"before={state.NinjutsuSlotBeforeTenPress} → after={state.NinjutsuSlotAfterTenPress}");
            }

            if (state.MudraStepCallsThisFrame > 0)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Mudra step calls:");
                ImGui.TableNextColumn();
                ImGui.TextWrapped(
                    $"frame #{state.MudraStepFrameNumber} | calls={state.MudraStepCallsThisFrame} | " +
                    $"branch={state.MudraStepSlotBranch} | CanExecuteGcd={state.MudraStepCanExecuteGcd} | " +
                    $"WasLast T/C/J={state.MudraWasLastTen}/{state.MudraWasLastChi}/{state.MudraWasLastJin}");
            }

            // Kassatsu
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.Kassatsu, "Kassatsu:"));
            ImGui.TableNextColumn();
            if (state.HasKassatsu)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.8f, 1f), Loc.T(LocalizedStrings.Debug.JobActiveLabel, "Active"));
            }
            else
            {
                ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.JobInactiveLabel, "Inactive"));
            }

            // Ten Chi Jin
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.TenChiJin, "Ten Chi Jin:"));
            ImGui.TableNextColumn();
            if (state.HasTenChiJin)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Loc.TFormat(LocalizedStrings.Debug.TcjStacksFormat, "Active ({0} stacks)", state.TenChiJinStacks));
            }
            else
            {
                ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.JobInactiveLabel, "Inactive"));
            }

            if (state.HasTenChiJin)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Adjust Ten/Chi/Jin:");
                ImGui.TableNextColumn();
                ImGui.TextColored(new Vector4(0.8f, 0.9f, 1f, 1f),
                    $"{state.TcjTenAdjustedId} / {state.TcjChiAdjustedId} / {state.TcjJinAdjustedId}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("TryGetNextStep:");
                ImGui.TableNextColumn();
                ImGui.TextWrapped(string.IsNullOrEmpty(state.TcjTryGetNextStepResult)
                    ? "—"
                    : state.TcjTryGetNextStepResult);
            }
        }, 140f);
    }

    private static void DrawGaugeSection(HermesDebugState state)
    {
        DebugTabHelpers.DrawSection(Loc.T(LocalizedStrings.Debug.Gauge, "Gauge"), "NinGaugeTable", () =>
        {
            // Ninki
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.Ninki, "Ninki:"));
            ImGui.TableNextColumn();
            var ninkiPercent = state.Ninki / 100f;
            ImGui.ProgressBar(ninkiPercent, new Vector2(-1, 0), $"{state.Ninki}/100");

            // Kazematoi
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.Kazematoi, "Kazematoi:"));
            ImGui.TableNextColumn();
            var kazColor = state.Kazematoi >= 4 ? new Vector4(0.5f, 1f, 0.5f, 1f) : new Vector4(0.7f, 0.7f, 0.7f, 1f);
            ImGui.TextColored(kazColor, $"{state.Kazematoi}/5");

            // Combo Step
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.Combo, "Combo:"));
            ImGui.TableNextColumn();
            var comboColor = state.ComboStep > 0 ? new Vector4(0.5f, 1f, 0.5f, 1f) : new Vector4(0.7f, 0.7f, 0.7f, 1f);
            ImGui.TextColored(comboColor, state.ComboStep > 0 ? $"Step {state.ComboStep} ({state.ComboTimeRemaining:F1}s)" : Loc.T(LocalizedStrings.Debug.NoneLabel, "None"));
        }, 140f);
    }

    private static void DrawBuffSection(HermesDebugState state)
    {
        DebugTabHelpers.DrawSection(Loc.T(LocalizedStrings.Debug.Buffs, "Buffs"), "NinBuffTable", () =>
        {
            // Suiton
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.Suiton, "Shadow Walker:"));
            ImGui.TableNextColumn();
            if (state.HasSuiton)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), $"{state.SuitonRemaining:F1}s");
            }
            else
            {
                ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.JobInactiveLabel, "Inactive"));
            }

            // Bunshin
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.Bunshin, "Bunshin:"));
            ImGui.TableNextColumn();
            if (state.HasBunshin)
            {
                ImGui.TextColored(new Vector4(1f, 0.6f, 0.2f, 1f), Loc.TFormat(LocalizedStrings.Debug.StacksFormat, "{0} stacks", state.BunshinStacks));
            }
            else
            {
                ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.JobInactiveLabel, "Inactive"));
            }

            // Phantom Kamaitachi
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.PhantomReady, "Phantom Ready:"));
            ImGui.TableNextColumn();
            DrawProcStatus(state.HasPhantomKamaitachiReady);

            // Raiju Ready
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.RaijuReady, "Raiju Ready:"));
            ImGui.TableNextColumn();
            if (state.HasRaijuReady)
            {
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), Loc.TFormat(LocalizedStrings.Debug.StacksFormat, "{0} stacks", state.RaijuStacks));
            }
            else
            {
                ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.No, "No"));
            }

            // Tenri Jindo
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.TenriJindoReady, "Tenri Jindo Ready:"));
            ImGui.TableNextColumn();
            DrawProcStatus(state.HasTenriJindoReady);
        }, 140f);
    }

    private static void DrawDebuffSection(HermesDebugState state)
    {
        DebugTabHelpers.DrawSection(Loc.T(LocalizedStrings.Debug.TargetDebuffs, "Target Debuffs"), "NinDebuffTable", () =>
        {
            // Kunai's Bane
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.KunaisBane, "Kunai's Bane:"));
            ImGui.TableNextColumn();
            if (state.HasKunaisBaneOnTarget)
            {
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), $"{state.KunaisBaneRemaining:F1}s");
            }
            else
            {
                ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.NotApplied, "Not applied"));
            }

            // Dokumori
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.Dokumori, "Dokumori:"));
            ImGui.TableNextColumn();
            if (state.HasDokumoriOnTarget)
            {
                ImGui.TextColored(new Vector4(0.8f, 0.5f, 1f, 1f), $"{state.DokumoriRemaining:F1}s");
            }
            else
            {
                ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.NotApplied, "Not applied"));
            }

            // Burst windows
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("In Mug:");
            ImGui.TableNextColumn();
            DrawProcStatus(state.InMug);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("In Trick Attack:");
            ImGui.TableNextColumn();
            DrawProcStatus(state.InTrickAttack);
        }, 140f);
    }

    private static void DrawPositionalSection(HermesDebugState state)
    {
        DebugTabHelpers.DrawSection(Loc.T(LocalizedStrings.Debug.Positional, "Positional"), "NinPositionalTable", () =>
        {
            // Current Position
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.Position, "Position:"));
            ImGui.TableNextColumn();
            if (state.TargetHasPositionalImmunity)
            {
                ImGui.TextColored(new Vector4(0.5f, 0.8f, 1f, 1f), Loc.T(LocalizedStrings.Debug.ImmuneOmni, "Immune (omni)"));
            }
            else if (state.IsAtRear)
            {
                ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), Loc.T(LocalizedStrings.Debug.Rear, "Rear"));
            }
            else if (state.IsAtFlank)
            {
                ImGui.TextColored(new Vector4(1f, 1f, 0.5f, 1f), Loc.T(LocalizedStrings.Debug.Flank, "Flank"));
            }
            else
            {
                ImGui.TextColored(new Vector4(1f, 0.5f, 0.5f, 1f), Loc.T(LocalizedStrings.Debug.Front, "Front"));
            }

            // True North
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.TrueNorth, "True North:"));
            ImGui.TableNextColumn();
            DrawProcStatus(state.HasTrueNorth);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Positional vNav:");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(string.IsNullOrEmpty(state.PositionalMovementPhase)
                ? "—"
                : $"{state.PositionalMovementPhase} {state.PositionalMovementSkipReason}".Trim());

            // Target
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.TargetLabel, "Target:"));
            ImGui.TableNextColumn();
            ImGui.Text(string.IsNullOrEmpty(state.CurrentTarget) ? Loc.T(LocalizedStrings.Debug.NoneLabel, "None") : state.CurrentTarget);

            EnemyPackDebugHelper.DrawEnemyPackTableRows(state, JobAoERadiusYalms.Melee);
        }, 140f);
    }

    private static void DrawProcStatus(bool hasProc)
    {
        if (hasProc)
        {
            ImGui.TextColored(new Vector4(0.5f, 1f, 0.5f, 1f), Loc.T(LocalizedStrings.Debug.Ready, "Ready"));
        }
        else
        {
            ImGui.TextDisabled(Loc.T(LocalizedStrings.Debug.No, "No"));
        }
    }

    private static void DrawNinjutsuGateSection(HermesDebugState state)
    {
        DebugTabHelpers.DrawSection("Ninjutsu Gates", "NinGateTable", () =>
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Module reached gates:");
            ImGui.TableNextColumn();
            DrawProcStatus(state.NinjutsuEvaluated);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("EnableNinjutsu:");
            ImGui.TableNextColumn();
            DrawProcStatus(state.EnableNinjutsu);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("NeedsSuiton:");
            ImGui.TableNextColumn();
            var needsSuitonColor = state.NeedsSuiton
                ? new Vector4(0.5f, 1f, 0.5f, 1f)
                : new Vector4(0.7f, 0.7f, 0.7f, 1f);
            ImGui.TextColored(needsSuitonColor, state.NeedsSuiton ? "Yes" : "No");
            if (!string.IsNullOrEmpty(state.NeedsSuitonReason))
                ImGui.TextWrapped(state.NeedsSuitonReason);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("ShouldStartNinjutsu:");
            ImGui.TableNextColumn();
            var startColor = state.ShouldStartNinjutsu
                ? new Vector4(0.5f, 1f, 0.5f, 1f)
                : new Vector4(1f, 0.5f, 0.5f, 1f);
            ImGui.TextColored(startColor, state.ShouldStartNinjutsu ? "Yes" : "No");
            if (!string.IsNullOrEmpty(state.ShouldStartNinjutsuBlockReason))
                ImGui.TextWrapped(state.ShouldStartNinjutsuBlockReason);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("IsInBurstPhase:");
            ImGui.TableNextColumn();
            DrawProcStatus(state.IsInBurstPhase);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("CanPushTenChiJin:");
            ImGui.TableNextColumn();
            DrawProcStatus(state.CanPushTenChiJinOgcd);
            if (!state.CanPushTenChiJinOgcd && !string.IsNullOrEmpty(state.TcjOgcdBlockReason))
                ImGui.TextWrapped(state.TcjOgcdBlockReason);

            if (state.IsMudraSequenceActive)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Live slot (AdjustId Ninjutsu):");
                ImGui.TableNextColumn();
                ImGui.TextWrapped(
                    $"{state.NinjutsuSlotAdjustedId} = {state.NinjutsuSlotProbe} | branch={state.MudraStepSlotBranch}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("TryExecuteStep / frame:");
                ImGui.TableNextColumn();
                ImGui.TextWrapped(
                    $"f#{state.MudraStepFrameNumber} calls={state.MudraStepCallsThisFrame} | " +
                    $"CanExecuteGcd={state.MudraStepCanExecuteGcd} | " +
                    $"WasLast T/C/J={state.MudraWasLastTen}/{state.MudraWasLastChi}/{state.MudraWasLastJin}");

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Ten pressability:");
            ImGui.TableNextColumn();
            ImGui.TextWrapped(
                $"{state.MudraTenCharges}/{state.MudraTenMaxCharges} charges | " +
                $"next +{state.MudraTenCooldownRemaining:F2}s | " +
                $"pressable in {state.MudraTenSecondsUntilPressable:F2}s | " +
                $"GetActionStatus(Action)={state.MudraTenActionStatus} | Event={state.MudraTenEventStatus}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Stuck counter:");
                ImGui.TableNextColumn();
                ImGui.TextColored(
                    state.MudraCountZeroStuckFrames >= state.MudraCountZeroStuckThreshold
                        ? new Vector4(1f, 0.5f, 0.5f, 1f)
                        : new Vector4(0.8f, 0.9f, 1f, 1f),
                    $"{state.MudraCountZeroStuckFrames} / {state.MudraCountZeroStuckThreshold}");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("Next mudra CanExecute:");
                ImGui.TableNextColumn();
                var nextName = string.IsNullOrEmpty(state.MudraNextName) ? "—" : state.MudraNextName;
                DrawProcStatus(state.MudraNextCanExecute);
                ImGui.TextDisabled($"{nextName} (CanExecuteActionId)");

                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text("GCD / anim lock:");
                ImGui.TableNextColumn();
                ImGui.TextWrapped(
                    $"CanExecuteGcd: {(state.MudraStuckCanExecuteGcd ? "Yes" : "No")} | " +
                    $"GCD rem: {state.MudraStuckGcdRemaining:F2}s | " +
                    $"Anim: {state.MudraStuckAnimationLock:F2}s");
            }
        }, 140f);
    }

    private static void DrawModuleStates(HermesDebugState state)
    {
        DebugTabHelpers.DrawSection(Loc.T(LocalizedStrings.Debug.ModuleStatesHeader, "Module States"), "NinModuleTable", () =>
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.DamageStateLabel, "Damage:"));
            ImGui.TableNextColumn();
            ImGui.TextDisabled(string.IsNullOrEmpty(state.DamageState) ? "—" : state.DamageState);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(Loc.T(LocalizedStrings.Debug.BuffStateLabel, "Buff:"));
            ImGui.TableNextColumn();
            ImGui.TextDisabled(string.IsNullOrEmpty(state.BuffState) ? "—" : state.BuffState);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Ninjutsu:");
            ImGui.TableNextColumn();
            ImGui.TextDisabled(string.IsNullOrEmpty(state.NinjutsuState) ? "—" : state.NinjutsuState);
        });
    }
}
