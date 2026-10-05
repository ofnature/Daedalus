namespace Daedalus.Config;

/// <summary>
/// Which navmesh plugin carries out Daedalus's own movement (walk-ins, positional hops, raise walks,
/// farm travel). Mirrors Minerva's "Navmesh plugin" choice, and should match it.
/// <para>
/// Ariadne is a fork of vnavmesh, and both take over the game's movement input. With both loaded and
/// two plugins each driving a different one, a walk reports itself running while the character never
/// moves — the roommate's Astrologian, 2026-10-04: 54 seconds stood still while Minerva "walked" her
/// through Ariadne, every hard cast held because steering zeroes the cast budget.
/// </para>
/// </summary>
public enum NavmeshPlugin
{
    /// <summary>Ariadne when it is loaded, otherwise vnavmesh — Minerva's order. The default.</summary>
    Auto = 0,

    /// <summary>Ariadne only (<c>Ariadne.*</c> IPC).</summary>
    Ariadne = 1,

    /// <summary>vnavmesh only (<c>vnavmesh.*</c> IPC).</summary>
    Vnavmesh = 2,
}

/// <summary>Picks the plugin. Pure, so the rule is testable.</summary>
public static class NavmeshPluginChoice
{
    public const string AriadneName = "Ariadne";
    public const string VnavmeshName = "vnavmesh";

    /// <summary>
    /// The plugin to drive. A named plugin is used even when it is not loaded — movement then reports
    /// unavailable rather than quietly going through the other one, so the setting never lies about
    /// which plugin moves the character.
    /// </summary>
    public static string Resolve(NavmeshPlugin choice, bool ariadneLoaded) => choice switch
    {
        NavmeshPlugin.Ariadne => AriadneName,
        NavmeshPlugin.Vnavmesh => VnavmeshName,
        _ => ariadneLoaded ? AriadneName : VnavmeshName,
    };
}
