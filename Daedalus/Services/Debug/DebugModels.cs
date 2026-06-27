using System;
using System.Collections.Generic;
using Daedalus.Models;
using Daedalus.Models.Action;
using Daedalus.Services.Action;

namespace Daedalus.Services.Debug;

/// <summary>
/// Complete debug state snapshot captured once per frame.
/// </summary>
public sealed class DebugSnapshot
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public uint ActiveJobId { get; init; }
    public DebugStatistics Statistics { get; init; } = new();
    public DebugGcdState GcdState { get; init; } = new();
    public DebugRotationState Rotation { get; init; } = new();
    public DebugTankState? Tank { get; init; }
    public DebugHealingState Healing { get; init; } = new();
    public DebugActionState Actions { get; init; } = new();
    public DebugOverhealStats OverhealStats { get; init; } = new();
}

/// <summary>
/// Performance statistics for the Performance tab.
/// </summary>
public sealed class DebugStatistics
{
    // Action tracking
    public int TotalAttempts { get; init; }
    public int SuccessCount { get; init; }
    public float SuccessRate { get; init; }

    // GCD uptime
    public float GcdUptime { get; init; }
    public float AverageCastGap { get; init; }

    // Most common failure
    public string? TopFailureReason { get; init; }
    public int TopFailureCount { get; init; }

    // Downtime tracking
    public int DowntimeEventCount { get; init; }
    public DateTime LastDowntimeTime { get; init; }
    public string LastDowntimeReason { get; init; } = "";
}

/// <summary>
/// Current GCD and action system state.
/// </summary>
public sealed class DebugGcdState
{
    public GcdState State { get; init; }
    public float GcdRemaining { get; init; }
    public float AnimationLockRemaining { get; init; }
    public bool IsCasting { get; init; }
    public bool CanExecuteGcd { get; init; }
    public bool CanExecuteOgcd { get; init; }
    public int WeaveSlots { get; init; }
    public string LastActionName { get; init; } = "None";

    // Legacy debug flags from ActionTracker
    public bool DebugGcdReady { get; init; }
    public bool DebugIsActive { get; init; }
}

/// <summary>
/// Rotation planning state for the Overview and Why Stuck tabs.
/// </summary>
public sealed class DebugRotationState
{
    // Core state
    public string PlanningState { get; init; } = "Idle";
    public string PlannedAction { get; init; } = "None";
    public string DpsState { get; init; } = "Idle";
    public string TargetInfo { get; init; } = "None";
    public string TargetDistanceInfo { get; init; } = "None";

    /// <summary>Non-empty when the whole rotation is globally paused this frame (explains a full stall). All jobs.</summary>
    public string PauseReason { get; init; } = "";

    // Resurrection
    public string RaiseState { get; init; } = "Idle";
    public string RaiseTarget { get; init; } = "None";

    // Esuna
    public string EsunaState { get; init; } = "Idle";
    public string EsunaTarget { get; init; } = "None";

    // oGCD States
    public string ThinAirState { get; init; } = "Idle";
    public string AsylumState { get; init; } = "Idle";
    public string AsylumTarget { get; init; } = "None";
    public string DefensiveState { get; init; } = "Idle";
    public string TemperanceState { get; init; } = "Idle";
    public string SurecastState { get; init; } = "Idle";
    public string PoMState { get; init; } = "Idle";

    // DPS Details
    public string AoEDpsState { get; init; } = "Idle";
    public int AoEDpsEnemyCount { get; init; }
    public int AoEDpsEngagedCount { get; init; }
    public float AoEDpsRadiusYalms { get; init; }
    public string MiseryState { get; init; } = "Idle";

    // Resources
    public int LilyCount { get; init; }
    public int BloodLilyCount { get; init; }
    public string LilyStrategy { get; init; } = "Balanced";
    public int SacredSightStacks { get; init; }
}

/// <summary>
/// Tank rotation debug state for the Why Stuck tab (normalized across PLD/WAR/DRK/GNB).
/// </summary>
public sealed class DebugTankState
{
    public string EnmityState { get; init; } = "Idle";
    public string MitigationState { get; init; } = "Idle";
    public string BuffState { get; init; } = "Idle";
    public string DamageState { get; init; } = "Idle";
    public string PlannedAction { get; init; } = "None";
    public string CurrentTarget { get; init; } = "None";
    public bool IsMainTank { get; init; }
    public string GaugeLabel { get; init; } = "Gauge";
    public int GaugeValue { get; init; }
    public int GaugeMax { get; init; } = 100;
    public int ComboStep { get; init; }
    public float ComboTimeRemaining { get; init; }
    /// <summary>Non-empty when the rotation is globally paused this frame (explains a full stall).</summary>
    public string PauseReason { get; init; } = "";
    /// <summary>Aggroed/engaged enemies within 25y (pull size).</summary>
    public int AggroedInRange { get; init; }
    /// <summary>Enemies within the job's self-centered AoE radius (5y for tanks) — what a PBAoE hits.</summary>
    public int InAoERange { get; init; }
    public IReadOnlyList<(string Label, string Value)> ResourceLines { get; init; } = [];
}

/// <summary>
/// Healing-related debug state.
/// </summary>
public sealed class DebugHealingState
{
    // AoE healing
    public string AoEStatus { get; init; } = "Idle";
    public int AoEInjuredCount { get; init; }
    public uint AoESelectedSpell { get; init; }
    public float PlayerHpPercent { get; init; }

    // Party info
    public int PartyListCount { get; init; }
    public int PartyValidCount { get; init; }
    public int BattleNpcCount { get; init; }
    public string NpcInfo { get; init; } = "";

    // Pending heals (HP prediction)
    public List<DebugPendingHeal> PendingHeals { get; init; } = new();
    public int TotalPendingHealAmount { get; init; }

    // Last heal calculation (for debugging formula)
    public int LastHealAmount { get; init; }
    public string LastHealStats { get; init; } = "";

    // Recent heals (legacy; healing tab uses per-ability last-used instead)
    public List<DebugRecentHeal> RecentHeals { get; init; } = new();
    public int TotalRecentHealAmount { get; init; }

    /// <summary>One row per healing ability with time since last use.</summary>
    public List<DebugHealingAbilityLastUsed> HealingAbilityLastUsed { get; init; } = new();

    // Sage Kardia (populated when Asclepius is the active rotation)
    public bool HasKardiaInfo { get; init; }
    public string KardiaState { get; init; } = "N/A";
    public ulong KardiaTargetGameObjectId { get; init; }
    public string KardiaTargetName { get; init; } = "None";
    public ulong TankGameObjectId { get; init; }
    public string TankTargetName { get; init; } = "None";
    public bool TankHasKardion { get; init; }
    public bool KardiaBlockedThisFrame { get; init; }
    public bool KardiaExecutedThisFrame { get; init; }
    public DateTime? KardiaLastCastUtc { get; init; }
    public DateTime? KardiaLastErrorUtc { get; init; }
    public string KardiaLastError { get; init; } = "None";

    // Shadow HP tracking
    public List<DebugShadowHpEntry> ShadowHpEntries { get; init; } = new();
}

/// <summary>
/// A pending heal from HP prediction.
/// </summary>
public sealed class DebugPendingHeal
{
    public uint TargetId { get; init; }
    public string TargetName { get; init; } = "";
    public int Amount { get; init; }
}

/// <summary>
/// Last-used timestamp for a healing ability in the debug panel.
/// </summary>
public sealed class DebugHealingAbilityLastUsed
{
    public uint ActionId { get; init; }
    public string ActionName { get; init; } = "";
    public SpellCategory Category { get; init; }
    public DateTime? LastUsedUtc { get; init; }
    public float? SecondsSinceLastUse =>
        LastUsedUtc is { } ts ? (float)Math.Max(0, (DateTime.UtcNow - ts).TotalSeconds) : null;
}

/// <summary>
/// A recent heal event.
/// </summary>
public sealed class DebugRecentHeal
{
    public DateTime Timestamp { get; init; }
    public uint ActionId { get; init; }
    public string ActionName { get; init; } = "";
    public string TargetName { get; init; } = "";
    public int Amount { get; init; }
    public float SecondsAgo => (float)Math.Max(0, (DateTime.UtcNow - Timestamp).TotalSeconds);
}

/// <summary>
/// Shadow HP tracking entry.
/// </summary>
public sealed class DebugShadowHpEntry
{
    public uint EntityId { get; init; }
    public string EntityName { get; init; } = "";
    public uint GameHp { get; init; }
    public uint ShadowHp { get; init; }
    public uint MaxHp { get; init; }
    public int Delta => (int)ShadowHp - (int)GameHp;
    public float GameHpPercent => MaxHp > 0 ? GameHp * 100f / MaxHp : 0;
    public float ShadowHpPercent => MaxHp > 0 ? ShadowHp * 100f / MaxHp : 0;
}

/// <summary>
/// Action-related debug state.
/// </summary>
public sealed class DebugActionState
{
    // History
    public IReadOnlyList<ActionAttempt> History { get; init; } = Array.Empty<ActionAttempt>();

    // Spell usage counts
    public List<DebugSpellUsage> SpellUsage { get; init; } = new();
}

/// <summary>
/// Spell usage count entry.
/// </summary>
public sealed class DebugSpellUsage
{
    public string Name { get; init; } = "";
    public uint ActionId { get; init; }
    public int Count { get; init; }
}

/// <summary>
/// Overheal statistics for the Overheal tab.
/// </summary>
public sealed class DebugOverhealStats
{
    public DateTime SessionStartTime { get; init; } = DateTime.Now;
    public TimeSpan SessionDuration { get; init; }
    public int TotalHealing { get; init; }
    public int TotalOverheal { get; init; }
    public float OverhealPercent { get; init; }
    public int EffectiveHealing => TotalHealing - TotalOverheal;
    public List<DebugSpellOverheal> BySpell { get; init; } = new();
    public List<DebugTargetOverheal> ByTarget { get; init; } = new();
    public List<DebugOverhealEvent> RecentOverheals { get; init; } = new();
}

/// <summary>
/// Per-spell overheal statistics.
/// </summary>
public sealed class DebugSpellOverheal
{
    public string SpellName { get; init; } = "";
    public uint ActionId { get; init; }
    public int TotalHealing { get; init; }
    public int TotalOverheal { get; init; }
    public int CastCount { get; init; }
    public float OverhealPercent => TotalHealing > 0 ? (float)TotalOverheal / TotalHealing * 100f : 0f;
}

/// <summary>
/// Per-target overheal statistics.
/// </summary>
public sealed class DebugTargetOverheal
{
    public string TargetName { get; init; } = "";
    public uint TargetId { get; init; }
    public int TotalHealing { get; init; }
    public int TotalOverheal { get; init; }
    public int HealCount { get; init; }
    public float OverhealPercent => TotalHealing > 0 ? (float)TotalOverheal / TotalHealing * 100f : 0f;
}

/// <summary>
/// A single overheal event for the timeline.
/// </summary>
public sealed class DebugOverhealEvent
{
    public DateTime Timestamp { get; init; }
    public string SpellName { get; init; } = "";
    public string TargetName { get; init; } = "";
    public int HealAmount { get; init; }
    public int OverhealAmount { get; init; }
    public float SecondsAgo => (float)Math.Max(0, (DateTime.UtcNow - Timestamp).TotalSeconds);
    public float OverhealPercent => HealAmount > 0 ? (float)OverhealAmount / HealAmount * 100f : 0f;
}
