namespace Daedalus.Config;

/// <summary>
/// Settings for the built-in DPS parser (Parser window + DpsMeterService).
/// </summary>
public sealed class ParserConfig
{
    /// <summary>Master toggle — when off, no damage events are accumulated.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Borderless overlay mode: no title bar, semi-transparent, compact rows.</summary>
    public bool BorderlessMode { get; set; } = false;

    /// <summary>In borderless mode, ignore mouse input so clicks pass through to the game.</summary>
    public bool ClickThrough { get; set; } = false;

    /// <summary>Hide the window entirely while no encounter is running.</summary>
    public bool HideOutOfCombat { get; set; } = false;

    /// <summary>
    /// Broadcast this toon's own parse over IPC/LAN (~2s cadence in combat) and accept other
    /// Daedalus toons' reports as authoritative. Requires the LAN coordinator to be enabled.
    /// </summary>
    public bool ShareOverNetwork { get; set; } = true;

    /// <summary>Replace character names with mythological aliases (stream/screenshot safe).</summary>
    public bool ScrambleNames { get; set; } = false;

    /// <summary>How many ended fights to keep in the history dropdown.</summary>
    public int FightHistoryCount { get; set; } = 10;
}
