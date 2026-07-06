using System;

namespace Daedalus.Config.DPS;

/// <summary>
/// Ninja (Hermes) configuration options.
/// Controls mudra system, Ninki gauge, and trick attack windows.
/// </summary>
public sealed class NinjaConfig
{
    #region Damage Toggles

    /// <summary>
    /// Whether to use AoE combo rotation.
    /// </summary>
    public bool EnableAoERotation { get; set; } = true;

    /// <summary>
    /// Whether to use Ninjutsu abilities.
    /// </summary>
    public bool EnableNinjutsu { get; set; } = true;

    /// <summary>
    /// Whether to use Bhavacakra (single-target Ninki spender).
    /// </summary>
    public bool EnableBhavacakra { get; set; } = true;

    /// <summary>
    /// Whether to use Hellfrog Medium (AoE Ninki spender).
    /// </summary>
    public bool EnableHellfrogMedium { get; set; } = true;

    /// <summary>
    /// Whether to use Phantom Kamaitachi.
    /// </summary>
    public bool EnablePhantomKamaitachi { get; set; } = true;

    /// <summary>
    /// Whether to use Forked/Fleeting Raiju procs.
    /// </summary>
    public bool EnableRaiju { get; set; } = true;

    /// <summary>
    /// Whether to use Dream Within a Dream / Assassinate.
    /// </summary>
    public bool EnableDreamWithinADream { get; set; } = true;

    #endregion

    #region Buff Toggles

    /// <summary>
    /// Whether to use Kunai's Bane (formerly Trick Attack).
    /// </summary>
    public bool EnableKunaisBane { get; set; } = true;

    /// <summary>
    /// Whether to use Kassatsu.
    /// </summary>
    public bool EnableKassatsu { get; set; } = true;

    /// <summary>
    /// Whether to use Ten Chi Jin.
    /// </summary>
    public bool EnableTenChiJin { get; set; } = true;

    /// <summary>
    /// Whether to use Bunshin.
    /// </summary>
    public bool EnableBunshin { get; set; } = true;

    /// <summary>
    /// Whether to use Meisui (converts Suiton to Ninki).
    /// </summary>
    public bool EnableMeisui { get; set; } = true;

    /// <summary>
    /// Whether to use Mug / Dokumori.
    /// </summary>
    public bool EnableMug { get; set; } = true;

    /// <summary>
    /// Whether to use Tenri Jindo (follow-up after Kunai's Bane).
    /// </summary>
    public bool EnableTenriJindo { get; set; } = true;

    /// <summary>
    /// Whether to use Feint for enemy damage reduction.
    /// </summary>
    public bool EnableFeint { get; set; } = true;

    #endregion

    #region Ninki Gauge Settings

    /// <summary>
    /// Minimum Ninki gauge to use Bhavacakra/Hellfrog.
    /// </summary>
    private int _ninkiMinGauge = 50;
    public int NinkiMinGauge
    {
        get => _ninkiMinGauge;
        set => _ninkiMinGauge = Math.Clamp(value, 50, 100);
    }

    /// <summary>
    /// Ninki threshold to dump gauge before overcapping.
    /// </summary>
    private int _ninkiOvercapThreshold = 80;
    public int NinkiOvercapThreshold
    {
        get => _ninkiOvercapThreshold;
        set => _ninkiOvercapThreshold = Math.Clamp(value, 50, 100);
    }

    #endregion

    #region Mudra Settings

    /// <summary>
    /// Whether to use Doton for AoE.
    /// </summary>
    public bool UseDotonForAoE { get; set; } = true;

    /// <summary>
    /// Minimum enemies for Doton placement.
    /// </summary>
    private int _dotonMinTargets = 3;
    public int DotonMinTargets
    {
        get => _dotonMinTargets;
        set => _dotonMinTargets = Math.Clamp(value, 2, 8);
    }

    #endregion

    #region Burst Window Settings

    /// <summary>
    /// Maximum seconds to hold Kunai's Bane waiting for party buffs.
    /// </summary>
    private float _kunaisBaneHoldTime = 3.0f;
    public float KunaisBaneHoldTime
    {
        get => _kunaisBaneHoldTime;
        set => _kunaisBaneHoldTime = Math.Clamp(value, 0f, 10f);
    }

    /// <summary>
    /// Hold Trick Attack / Kunai's Bane when the engaged pack's estimated time-to-kill (recent
    /// damage rate) is below this many seconds — a +10% debuff on a pack that dies in 3s is a
    /// wasted 60s cooldown, and Shadow Walker (20s) rides through the transit so the burst opens
    /// the NEXT pack at full value instead. 0 disables. Same TTK lesson as the MCH Queen hold:
    /// trash doesn't get low, it melts.
    /// </summary>
    private int _trickMinPackTtkSeconds = 6;
    public int TrickMinPackTtkSeconds
    {
        get => _trickMinPackTtkSeconds;
        set => _trickMinPackTtkSeconds = Math.Clamp(value, 0, 20);
    }

    /// <summary>
    /// Save Ninki for burst windows.
    /// </summary>
    public bool SaveNinkiForBurst { get; set; } = false;

    /// <summary>
    /// Pool gauge resources (Bhavacakra, Hellfrog Medium) for raid buff burst windows.
    /// When disabled, Hermes uses ABB — press buttons on cooldown.
    /// </summary>
    public bool EnableBurstPooling { get; set; } = false;

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

    #region Positional Settings

    /// <summary>
    /// Whether to use vNav to reposition before Aeolian Edge / Armor Crush (SAM parity).
    /// </summary>
    public bool EnablePositionalMovement { get; set; } = true;

    /// <summary>
    /// Whether to enforce positional requirements.
    /// </summary>
    public bool EnforcePositionals { get; set; } = false;

    /// <summary>
    /// Allow weaponskills even without True North when out of position.
    /// </summary>
    public bool AllowPositionalLoss { get; set; } = true;

    #endregion
}
