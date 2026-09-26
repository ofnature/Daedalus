using System.Collections.Generic;
using Daedalus.Data;

namespace Daedalus.Config;

/// <summary>Bozja Lost Action settings. Only actions set to a duty action slot are ever used.</summary>
public sealed class BozjaConfig
{
    /// <summary>Master toggle for the Bozja Lost Action layer.</summary>
    public bool EnableLostActions { get; set; } = true;

    /// <summary>Party buffs and forges go on every party member in range, not just ourselves.</summary>
    public bool BuffParty { get; set; } = true;

    /// <summary>Lost Cures fire on party members below this fraction of max HP.</summary>
    public float HealHpPct { get; set; } = 0.60f;

    /// <summary>
    /// RSR's default: Lost Burst / Lost Rampage are used as AoE damage whenever 2+ enemies are within
    /// 10y. Off: only against an enemy with the matching aversion that lacks the debuff.
    /// </summary>
    public bool AversionAoeSpam { get; set; } = true;

    /// <summary>
    /// Per-action overrides of <see cref="LostActionDef.DefaultEnabled"/>, keyed by action id. Only what
    /// the user changed is stored, so a pre-populated default can never be merged back in on load.
    /// </summary>
    public Dictionary<uint, bool> ActionEnabled { get; set; } = [];

    public bool IsEnabled(LostActionDef def)
        => ActionEnabled.TryGetValue(def.ActionId, out var on) ? on : def.DefaultEnabled;
}
