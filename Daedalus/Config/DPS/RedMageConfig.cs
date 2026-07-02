using System;

namespace Daedalus.Config.DPS;

/// <summary>
/// Red Mage (Circe) configuration options.
/// Controls Dualcast system, mana balance, and melee combo timing.
/// </summary>
public sealed class RedMageConfig
{
    #region Damage Toggles

    /// <summary>
    /// Whether to use AoE rotation.
    /// </summary>
    public bool EnableAoERotation { get; set; } = true;

    /// <summary>
    /// Whether to use Verstone/Verfire procs.
    /// </summary>
    public bool EnableProcs { get; set; } = true;

    /// <summary>
    /// Whether to use melee combo (Riposte → Zwerchhau → Redoublement).
    /// </summary>
    public bool EnableMeleeCombo { get; set; } = true;

    /// <summary>
    /// Whether to use finisher combo (Verholy/Verflare → Scorch → Resolution).
    /// </summary>
    public bool EnableFinisherCombo { get; set; } = true;

    /// <summary>
    /// Whether to use Grand Impact procs.
    /// </summary>
    public bool EnableGrandImpact { get; set; } = true;

    #endregion

    #region Buff Toggles

    /// <summary>
    /// Whether to use Embolden (party buff).
    /// </summary>
    public bool EnableEmbolden { get; set; } = true;

    /// <summary>
    /// Whether to use Manafication.
    /// </summary>
    public bool EnableManafication { get; set; } = true;

    /// <summary>
    /// Whether to use Acceleration.
    /// </summary>
    public bool EnableAcceleration { get; set; } = true;

    /// <summary>
    /// Whether to use Addle for enemy magic damage reduction.
    /// </summary>
    public bool EnableAddle { get; set; } = true;

    #endregion

    #region oGCD Toggles

    /// <summary>
    /// Whether to use Fleche.
    /// </summary>
    public bool EnableFleche { get; set; } = true;

    /// <summary>
    /// Whether to use Contre Sixte.
    /// </summary>
    public bool EnableContreSixte { get; set; } = true;

    /// <summary>
    /// Whether to use Corps-a-corps.
    /// </summary>
    public bool EnableCorpsACorps { get; set; } = true;

    /// <summary>
    /// Whether to use Engagement/Displacement.
    /// </summary>
    public bool EnableEngagement { get; set; } = true;

    /// <summary>
    /// Prefer Engagement over Displacement (safer).
    /// </summary>
    public bool PreferEngagementOverDisplacement { get; set; } = true;

    /// <summary>
    /// Minimum player HP percent required to fire Corps-a-corps / Engagement.
    /// Prevents dashing into boss mechanics when HP is low.
    /// Range: 0.0 (always allowed) to 1.0 (only at full HP).
    /// </summary>
    private float _meleeDashMinHpPercent = 0.70f;
    public float MeleeDashMinHpPercent
    {
        get => _meleeDashMinHpPercent;
        set => _meleeDashMinHpPercent = Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// Whether to use Vice of Thorns (follow-up after Embolden).
    /// </summary>
    public bool EnableViceOfThorns { get; set; } = true;

    /// <summary>
    /// Whether to use Prefulgence (follow-up after Manafication).
    /// </summary>
    public bool EnablePrefulgence { get; set; } = true;

    #endregion

    #region Mana Balance Settings

    /// <summary>
    /// Minimum mana to enter melee combo.
    /// </summary>
    private int _meleeComboMinMana = 50;
    public int MeleeComboMinMana
    {
        get => _meleeComboMinMana;
        set => _meleeComboMinMana = Math.Clamp(value, 50, 100);
    }

    /// <summary>
    /// Seconds after entering combat before melee combo entry is allowed.
    /// Prevents Riposte on pull when gauge/combo state carries over from downtime.
    /// Set to 0 to allow immediate entry when mana thresholds are met.
    /// </summary>
    private float _meleeComboMinCombatSeconds = 4f;
    public float MeleeComboMinCombatSeconds
    {
        get => _meleeComboMinCombatSeconds;
        set => _meleeComboMinCombatSeconds = Math.Clamp(value, 0f, 30f);
    }

    /// <summary>
    /// Maximum mana imbalance before prioritizing the lower color.
    /// </summary>
    private int _manaImbalanceThreshold = 30;
    public int ManaImbalanceThreshold
    {
        get => _manaImbalanceThreshold;
        set => _manaImbalanceThreshold = Math.Clamp(value, 10, 50);
    }

    /// <summary>
    /// Whether to strictly balance mana (prioritize lower).
    /// </summary>
    public bool StrictManaBalance { get; set; } = true;

    #endregion

    #region Melee Combo Settings

    /// <summary>
    /// Use melee combo during burst windows.
    /// </summary>
    public bool UseMeleeDuringBurst { get; set; } = true;

    /// <summary>
    /// Hold melee combo for Embolden if close.
    /// </summary>
    public bool HoldMeleeForEmbolden { get; set; } = true;

    /// <summary>
    /// Maximum seconds to hold melee combo waiting for Embolden.
    /// </summary>
    private float _meleeHoldForEmbolden = 5.0f;
    public float MeleeHoldForEmbolden
    {
        get => _meleeHoldForEmbolden;
        set => _meleeHoldForEmbolden = Math.Clamp(value, 0f, 15f);
    }

    /// <summary>
    /// Verholy vs Verflare preference.
    /// </summary>
    public FinisherPreference FinisherPreference { get; set; } = FinisherPreference.BalanceBased;

    #endregion

    #region Burst Window Settings

    /// <summary>
    /// Hold melee combo entry for raid buff burst windows.
    /// When enabled, delays melee combo when burst is imminent within 8s.
    /// </summary>
    public bool EnableBurstPooling { get; set; } = true;

    /// <summary>
    /// Maximum seconds to hold Embolden waiting for party buffs.
    /// </summary>
    private float _emboldenHoldTime = 3.0f;
    public float EmboldenHoldTime
    {
        get => _emboldenHoldTime;
        set => _emboldenHoldTime = Math.Clamp(value, 0f, 10f);
    }

    /// <summary>
    /// Use Manafication with melee combo.
    /// </summary>
    public bool UseManaficationWithMelee { get; set; } = true;

    /// <summary>
    /// Minimum target HP percent to start solo burst (Embolden/Manafication).
    /// </summary>
    private float _soloBurstMinTargetHpPercent = 0.15f;
    public float SoloBurstMinTargetHpPercent
    {
        get => _soloBurstMinTargetHpPercent;
        set => _soloBurstMinTargetHpPercent = Math.Clamp(value, 0.05f, 0.50f);
    }

    /// <summary>
    /// Minimum enemies near the target to start solo burst.
    /// </summary>
    private int _soloBurstMinEnemies = 2;
    public int SoloBurstMinEnemies
    {
        get => _soloBurstMinEnemies;
        set => _soloBurstMinEnemies = Math.Clamp(value, 1, 8);
    }

    /// <summary>
    /// Max seconds between Embolden and Manafication cooldowns to treat them as a pair.
    /// </summary>
    private float _soloBurstPairCooldownSeconds = 5.0f;
    public float SoloBurstPairCooldownSeconds
    {
        get => _soloBurstPairCooldownSeconds;
        set => _soloBurstPairCooldownSeconds = Math.Clamp(value, 1f, 15f);
    }

    /// <summary>
    /// Preferred minimum lower mana before starting Manafication in solo burst (80|80+).
    /// Falls back to <see cref="MeleeComboMinMana"/> when both burst oGCDs are ready.
    /// </summary>
    private int _soloBurstIdealMinMana = 80;
    public int SoloBurstIdealMinMana
    {
        get => _soloBurstIdealMinMana;
        set => _soloBurstIdealMinMana = Math.Clamp(value, 50, 100);
    }

    /// <summary>
    /// Single-target solo burst gate: fire Manafication/Embolden on a lone target (boss, big
    /// trash) when its estimated time-to-death is at least this many seconds. 0 disables the
    /// TTK check (lone targets then never pass the pack gate).
    /// </summary>
    private float _soloBurstMinSingleTargetTtkSeconds = 10f;
    public float SoloBurstMinSingleTargetTtkSeconds
    {
        get => _soloBurstMinSingleTargetTtkSeconds;
        set => _soloBurstMinSingleTargetTtkSeconds = Math.Clamp(value, 0f, 60f);
    }

    #endregion

    #region AoE Settings

    /// <summary>
    /// Minimum enemies for AoE rotation.
    /// </summary>
    private int _aoEMinTargets = 3;
    public int AoEMinTargets
    {
        get => _aoEMinTargets;
        set => _aoEMinTargets = Math.Clamp(value, 2, 8);
    }

    #endregion

    #region Utility Settings

    /// <summary>
    /// Whether to use Verraise.
    /// </summary>
    public bool EnableVerraise { get; set; } = true;

    /// <summary>
    /// Whether to use Swiftcast/Dualcast for Verraise.
    /// </summary>
    public bool UseDualcastForVerraise { get; set; } = true;

    #endregion
}

/// <summary>
/// Verholy/Verflare finisher preference.
/// </summary>
public enum FinisherPreference
{
    /// <summary>
    /// Use finisher that balances mana (proc for lower).
    /// </summary>
    BalanceBased,

    /// <summary>
    /// Always prefer Verholy.
    /// </summary>
    PreferVerholy,

    /// <summary>
    /// Always prefer Verflare.
    /// </summary>
    PreferVerflare
}
