using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Ipc;

/// <summary>
/// RSR-compatible IPC surface. Plugins that drive RotationSolverReborn — most importantly
/// Questionable's kill-quest combat module — probe and control the rotation through two gates:
/// <c>RotationSolverReborn.Test</c> (availability check) and
/// <c>RotationSolverReborn.ChangeOperatingMode</c> (Manual on fight start, Off on fight end).
/// Registering the same gates lets those plugins drive Daedalus as a drop-in RSR replacement:
/// Questionable does its own kill-target selection (sets the hard target via ITargetManager),
/// so all Daedalus has to do is run the rotation while the override is active.
/// </summary>
/// <remarks>
/// The override is transient (<see cref="Configuration.QuestCombatOverride"/>, never persisted):
/// a crash mid-quest must not leave the rotation permanently enabled, and the user's master
/// switch / saved config is untouched by quest-driven starts and stops.
/// Registration is skipped when the real RSR plugin is loaded — Dalamud call gates are global,
/// and stealing RSR's gate names while it is running would break both plugins.
/// </remarks>
public sealed class RsrCompatIpc : IDisposable
{
    private const string RsrInternalName = "RotationSolverReborn";

    /// <summary>
    /// Mirror of RSR's <c>StateCommandType</c>. Member names AND numeric values must match RSR
    /// (Off=0, Auto=1, TargetOnly=2, Manual=3, AutoDuty=4, Henched=5) — callers like Questionable
    /// declare their own copy and Dalamud converts by serialized value across assemblies.
    /// Never reorder or insert members.
    /// </summary>
    public enum StateCommandType : byte
    {
        Off,
        Auto,
        TargetOnly,
        Manual,
        AutoDuty,
        Henched,
    }

    // Striking dummy BNpcName ids (541 = striking dummy, 13078 = timeworn striking dummy) —
    // same detection RSR uses in ObjectHelper.IsDummy, plus the timeworn variant.
    private static readonly uint[] TrainingDummyNameIds = [541, 13078];

    private readonly Configuration _configuration;
    private readonly IPluginLog _log;

    private readonly ICallGateProvider<string, object>? _test;
    private readonly ICallGateProvider<StateCommandType, object>? _changeOperatingMode;

    /// <summary>True when the RSR-compat gates were registered (real RSR not loaded, no gate error).</summary>
    public bool Registered { get; }

    /// <summary>Fired on the IPC caller's thread when the quest-combat override flips.</summary>
    public event Action<bool>? OverrideChanged;

    public RsrCompatIpc(IDalamudPluginInterface pluginInterface, Configuration configuration, IPluginLog log)
    {
        _configuration = configuration;
        _log = log;

        if (IsRealRsrLoaded(pluginInterface))
        {
            _log.Warning("RotationSolverReborn is loaded — skipping RSR-compat IPC registration (quest plugins will drive RSR, not Daedalus).");
            return;
        }

        try
        {
            _test = pluginInterface.GetIpcProvider<string, object>("RotationSolverReborn.Test");
            _test.RegisterAction(Test);

            _changeOperatingMode = pluginInterface.GetIpcProvider<StateCommandType, object>("RotationSolverReborn.ChangeOperatingMode");
            _changeOperatingMode.RegisterAction(ChangeOperatingMode);

            Registered = true;
            _log.Info("RSR-compat IPC registered (RotationSolverReborn.Test / .ChangeOperatingMode) — quest plugins can drive Daedalus.");
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to register RSR-compat IPC gates; quest-plugin integration disabled.");
            _test = null;
            _changeOperatingMode = null;
        }
    }

    /// <summary>Maps an RSR operating mode to "rotation should run". TargetOnly performs no actions.</summary>
    public static bool MapsToEnabled(StateCommandType stateCommand)
    {
        return stateCommand switch
        {
            StateCommandType.Off => false,
            StateCommandType.TargetOnly => false,
            _ => true,
        };
    }

    /// <summary>True for striking-dummy BNpcName ids — quest-driven combat must never grind on one.</summary>
    public static bool IsTrainingDummy(uint nameId)
    {
        return Array.IndexOf(TrainingDummyNameIds, nameId) >= 0;
    }

    private void Test(string param)
    {
        // Availability probe (Questionable calls this to decide RSR is present). No-op.
        _log.Debug("RSR-compat IPC Test called. Param: {0}", param);
    }

    private void ChangeOperatingMode(StateCommandType stateCommand)
    {
        var enable = MapsToEnabled(stateCommand);
        _log.Debug("RSR-compat IPC ChangeOperatingMode: {0} -> override {1}", stateCommand, enable ? "on" : "off");

        if (_configuration.QuestCombatOverride == enable)
            return;

        _configuration.QuestCombatOverride = enable;
        OverrideChanged?.Invoke(enable);
    }

    private static bool IsRealRsrLoaded(IDalamudPluginInterface pluginInterface)
    {
        try
        {
            return pluginInterface.InstalledPlugins.Any(p =>
                p.InternalName.Equals(RsrInternalName, StringComparison.OrdinalIgnoreCase) && p.IsLoaded);
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        _configuration.QuestCombatOverride = false;
        _test?.UnregisterAction();
        _changeOperatingMode?.UnregisterAction();
    }
}
