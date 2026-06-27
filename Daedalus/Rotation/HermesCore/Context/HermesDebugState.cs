using Daedalus.Data;
using Daedalus.Rotation.Common;

namespace Daedalus.Rotation.HermesCore.Context;

/// <summary>
/// Debug state for Ninja (Hermes) rotation.
/// Tracks rotation decisions and state for debug display.
/// </summary>
public sealed class HermesDebugState : IEnemyPackDebug
{
    // Module states
    public string DamageState { get; set; } = "";
    public string BuffState { get; set; } = "";
    public string NinjutsuState { get; set; } = "";

    // Current action planning
    public string PlannedAction { get; set; } = "";
    public string PlanningState { get; set; } = "";

    // Gauge tracking
    public int Ninki { get; set; }
    public int Kazematoi { get; set; }

    // Downtime probe — live mudra reservation vs true idle (open GCD with no mudra excuse)
    public bool IsGcdReadyRaw { get; set; }
    public bool IsMudraReservationWindow { get; set; }
    public bool IsTrueIdleDowntime { get; set; }
    public string DowntimeReservationReason { get; set; } = "";
    public float SessionMudraReservationSeconds { get; set; }
    public float SessionTrueIdleSeconds { get; set; }

    // Mudra state
    public bool IsMudraActive { get; set; }
    public bool HasGameMudraStatus { get; set; }
    public bool IsMudraSequenceActive { get; set; }
    public int MudraCount { get; set; }
    public string MudraSequence { get; set; } = "";
    public NINActions.NinjutsuType PendingNinjutsu { get; set; }
    public bool HasKassatsu { get; set; }
    public bool HasTenChiJin { get; set; }
    public int TenChiJinStacks { get; set; }

    // Live TCJ slot probe (NinjutsuModule → Hermes tab)
    public uint TcjTenAdjustedId { get; set; }
    public uint TcjChiAdjustedId { get; set; }
    public uint TcjJinAdjustedId { get; set; }
    public string TcjTryGetNextStepResult { get; set; } = "";
    public bool IsTcjStepPending { get; set; }

    // Ninjutsu gate diagnostics (live — Hermes debug tab)
    public bool NinjutsuEvaluated { get; set; }
    public bool EnableNinjutsu { get; set; }
    public bool NeedsSuiton { get; set; }
    public string NeedsSuitonReason { get; set; } = "";
    public bool ShouldStartNinjutsu { get; set; }
    public string ShouldStartNinjutsuBlockReason { get; set; } = "";
    public int NinjutsuAbortCooldownFrames { get; set; }
    public bool IsInBurstPhase { get; set; }
    public bool CanPushTenChiJinOgcd { get; set; }
    public string TcjOgcdBlockReason { get; set; } = "";

    // Mudra stuck probe (active sequence — slot-based RSR parity)
    public int MudraCountZeroStuckFrames { get; set; }
    public int MudraCountZeroStuckThreshold { get; set; }
    public uint NinjutsuSlotAdjustedId { get; set; }
    public string NinjutsuSlotProbe { get; set; } = "";
    public uint NinjutsuSlotFromActionManager { get; set; }
    public uint NinjutsuSlotBeforeTenPress { get; set; }
    public uint NinjutsuSlotAfterTenPress { get; set; }
    public ulong MudraStepFrameNumber { get; set; }
    public int MudraStepCallsThisFrame { get; set; }
    public string MudraStepSlotBranch { get; set; } = "";
    public bool MudraStepCanExecuteGcd { get; set; }
    public bool MudraWasLastTen { get; set; }
    public bool MudraWasLastChi { get; set; }
    public bool MudraWasLastJin { get; set; }
    public uint MudraTenActionStatus { get; set; }
    public uint MudraTenEventStatus { get; set; }
    public uint MudraTenCharges { get; set; }
    public uint MudraTenMaxCharges { get; set; }
    public float MudraTenCooldownRemaining { get; set; }
    public float MudraTenChargeRecastTotal { get; set; }
    public bool MudraTenIsPressable { get; set; }
    public float MudraTenSecondsUntilPressable { get; set; }
    public string MudraNextName { get; set; } = "";
    public bool MudraNextCanExecute { get; set; }
    public bool MudraStuckCanExecuteGcd { get; set; }
    public float MudraStuckGcdRemaining { get; set; }
    public float MudraStuckAnimationLock { get; set; }

    // Buff tracking
    public bool HasSuiton { get; set; }
    public float SuitonRemaining { get; set; }
    public bool HasBunshin { get; set; }
    public int BunshinStacks { get; set; }
    public bool HasPhantomKamaitachiReady { get; set; }
    public bool HasRaijuReady { get; set; }
    public int RaijuStacks { get; set; }
    public bool HasMeisui { get; set; }
    public bool HasTenriJindoReady { get; set; }

    // Debuff tracking
    public bool HasKunaisBaneOnTarget { get; set; }
    public float KunaisBaneRemaining { get; set; }
    public bool HasDokumoriOnTarget { get; set; }
    public float DokumoriRemaining { get; set; }
    public bool InMug { get; set; }
    public bool InTrickAttack { get; set; }

    // Combo tracking
    public int ComboStep { get; set; }
    public float ComboTimeRemaining { get; set; }

    // Positional tracking
    public bool IsAtRear { get; set; }
    public bool IsAtFlank { get; set; }
    public bool HasTrueNorth { get; set; }
    public bool TargetHasPositionalImmunity { get; set; }
    public string PositionalMovementPhase { get; set; } = "";
    public string PositionalMovementSkipReason { get; set; } = "";
    public string BurstApproachPhase { get; set; } = "";
    public string BurstApproachSkipReason { get; set; } = "";
    public bool BurstApproachInBurstPrep { get; set; }
    public bool BurstApproachKbInRange { get; set; }
    public bool BurstApproachHasTarget { get; set; }
    public string BurstApproachTargetName { get; set; } = "";

    // Targeting
    public string CurrentTarget { get; set; } = "";
    public int EngagedEnemies { get; set; }
    public int AoeRangeEnemies { get; set; }
    public int NearbyEnemies
    {
        get => AoeRangeEnemies;
        set => AoeRangeEnemies = value;
    }

    /// <summary>
    /// Gets a formatted string of the current mudra sequence.
    /// </summary>
    public static string FormatMudraSequence(NINActions.MudraType m1, NINActions.MudraType m2, NINActions.MudraType m3)
    {
        static char MudraChar(NINActions.MudraType m) => m switch
        {
            NINActions.MudraType.Ten => 'T',
            NINActions.MudraType.Chi => 'C',
            NINActions.MudraType.Jin => 'J',
            _ => '-'
        };

        return $"[{MudraChar(m1)}{MudraChar(m2)}{MudraChar(m3)}]";
    }

    /// <summary>Ten→Chi→Jin mudra slot pattern; debug tab shows as [TCJ].</summary>
    public static bool IsTcjMudraSequence(NINActions.MudraType m1, NINActions.MudraType m2, NINActions.MudraType m3) =>
        m1 == NINActions.MudraType.Ten
        && m2 == NINActions.MudraType.Chi
        && m3 == NINActions.MudraType.Jin;
}
