using System;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Daedalus.Rotation;
using Daedalus.Services.Targeting;

namespace Daedalus.Ipc;

/// <summary>
/// Provides IPC interface for external plugins to interact with Daedalus.
/// </summary>
/// <remarks>
/// Available IPC endpoints:
/// - Daedalus.Test: Availability check (no args, no return)
/// - Daedalus.IsEnabled: Check if rotation is enabled (returns bool)
/// - Daedalus.SetEnabled: Enable/disable rotation (takes bool)
/// - Daedalus.GetVersion: Get plugin version (returns string)
/// - Daedalus.GetActiveRotation: Get active rotation name (returns string or empty)
/// - Daedalus.GetSupportedJobs: Get array of supported job IDs (returns uint[])
/// - Daedalus.Targeting.RecordExternalWrite: Attribute a hard-target write to automation (takes ulong)
/// - Daedalus.OnStateChanged: Event fired when enabled state changes
/// </remarks>
public sealed class DaedalusIpc : IDisposable
{
    private readonly Configuration _configuration;
    private readonly Action _saveConfiguration;
    private readonly IPluginLog _log;
    private readonly Func<RotationManager?> _getRotationManager;
    private readonly string _version;

    // Basic endpoints
    private readonly ICallGateProvider<object> _test;
    private readonly ICallGateProvider<bool, object> _setEnabled;
    private readonly ICallGateProvider<bool> _isEnabled;

    // Extended endpoints
    private readonly ICallGateProvider<string> _getVersion;
    private readonly ICallGateProvider<string> _getActiveRotation;
    private readonly ICallGateProvider<uint[]> _getSupportedJobs;
    private readonly ICallGateProvider<ulong, object> _recordExternalTargetWrite;

    // Events
    private readonly ICallGateProvider<bool, object> _onStateChanged;

    public DaedalusIpc(
        IDalamudPluginInterface pluginInterface,
        Configuration configuration,
        Action saveConfiguration,
        IPluginLog log,
        string version = "",
        Func<RotationManager?>? getRotationManager = null)
    {
        _configuration = configuration;
        _saveConfiguration = saveConfiguration;
        _log = log;
        _version = string.IsNullOrEmpty(version) ? Plugin.PluginVersion : version;
        _getRotationManager = getRotationManager ?? (() => null);

        // Basic endpoints
        _test = pluginInterface.GetIpcProvider<object>("Daedalus.Test");
        _test.RegisterAction(Test);

        _setEnabled = pluginInterface.GetIpcProvider<bool, object>("Daedalus.SetEnabled");
        _setEnabled.RegisterAction(SetEnabled);

        _isEnabled = pluginInterface.GetIpcProvider<bool>("Daedalus.IsEnabled");
        _isEnabled.RegisterFunc(IsEnabled);

        // Extended endpoints
        _getVersion = pluginInterface.GetIpcProvider<string>("Daedalus.GetVersion");
        _getVersion.RegisterFunc(GetVersion);

        _getActiveRotation = pluginInterface.GetIpcProvider<string>("Daedalus.GetActiveRotation");
        _getActiveRotation.RegisterFunc(GetActiveRotation);

        _getSupportedJobs = pluginInterface.GetIpcProvider<uint[]>("Daedalus.GetSupportedJobs");
        _getSupportedJobs.RegisterFunc(GetSupportedJobs);

        _recordExternalTargetWrite = pluginInterface.GetIpcProvider<ulong, object>("Daedalus.Targeting.RecordExternalWrite");
        _recordExternalTargetWrite.RegisterAction(RecordExternalTargetWrite);

        // Event providers
        _onStateChanged = pluginInterface.GetIpcProvider<bool, object>("Daedalus.OnStateChanged");

        _log.Info("Daedalus IPC initialized (v{0})", _version);
    }

    #region Basic Endpoints

    /// <summary>
    /// Test endpoint for availability checks. Does nothing but confirms IPC is working.
    /// </summary>
    private void Test()
    {
        // No-op - just validates IPC is callable
    }

    /// <summary>
    /// Enables or disables the Daedalus rotation.
    /// Fires OnStateChanged event when state changes.
    /// </summary>
    /// <param name="enabled">True to enable, false to disable.</param>
    private void SetEnabled(bool enabled)
    {
        if (_configuration.Enabled != enabled)
        {
            _configuration.SetEnabledByUser(enabled);
            _saveConfiguration();

            // Fire state changed event
            try
            {
                _onStateChanged.SendMessage(enabled);
            }
            catch (Exception ex)
            {
                _log.Warning(ex, "Failed to send OnStateChanged event");
            }

            var status = enabled ? "enabled" : "disabled";
            _log.Info($"Daedalus {status} via IPC");
        }
    }

    /// <summary>
    /// Returns whether Daedalus rotation is currently enabled.
    /// </summary>
    private bool IsEnabled()
    {
        return _configuration.Enabled;
    }

    #endregion

    #region Extended Endpoints

    /// <summary>
    /// Returns the current plugin version.
    /// </summary>
    private string GetVersion()
    {
        return _version;
    }

    /// <summary>
    /// Returns the name of the currently active rotation, or empty string if none.
    /// </summary>
    private string GetActiveRotation()
    {
        var manager = _getRotationManager();
        return manager?.ActiveRotation?.Name ?? string.Empty;
    }

    /// <summary>
    /// Returns an array of all supported job IDs.
    /// </summary>
    private uint[] GetSupportedJobs()
    {
        var manager = _getRotationManager();
        if (manager == null)
            return Array.Empty<uint>();

        var jobSet = new System.Collections.Generic.HashSet<uint>();
        foreach (var rotation in manager.RegisteredRotations)
        {
            foreach (var jobId in rotation.SupportedJobIds)
            {
                jobSet.Add(jobId);
            }
        }
        return jobSet.ToArray();
    }

    #endregion

    #region Targeting

    /// <summary>
    /// Lets a companion automation plugin claim a hard-target write it is about to make, so the
    /// manual-control grace does not mistake it for the user clicking something.
    /// </summary>
    /// <remarks>
    /// <see cref="ManualControlGrace.RecordOwnWrite"/> is <c>public static</c> and therefore
    /// in-process only: Dalamud call gates are per-process, so a plugin in another assembly — or
    /// another game client on the same box — cannot reach it. Without this endpoint a target
    /// written by Theseus (or anything else driving us) is classified as a user click and
    /// suppresses our own movement pulses for the full four-second grace, which does not error and
    /// does not log; it just makes automated movement mysteriously stutter.
    ///
    /// <para>
    /// Callers must invoke this <b>immediately before</b> writing the target. Attribution only
    /// holds for a second, deliberately, so a stale claim cannot launder a later genuine click.
    /// </para>
    /// </remarks>
    /// <param name="targetId">GameObjectId the caller is about to write.</param>
    private void RecordExternalTargetWrite(ulong targetId)
    {
        ManualControlGrace.RecordOwnWrite(targetId);
    }

    #endregion

    /// <summary>
    /// Notifies external plugins that the enabled state has changed.
    /// Call this when state changes from sources other than IPC (e.g., UI, command).
    /// </summary>
    public void NotifyStateChanged(bool enabled)
    {
        try
        {
            _onStateChanged.SendMessage(enabled);
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Failed to send OnStateChanged event");
        }
    }

    public void Dispose()
    {
        _test.UnregisterAction();
        _setEnabled.UnregisterAction();
        _isEnabled.UnregisterFunc();
        _getVersion.UnregisterFunc();
        _getActiveRotation.UnregisterFunc();
        _getSupportedJobs.UnregisterFunc();
        _recordExternalTargetWrite.UnregisterAction();

        // _onStateChanged is a SendMessage-only provider (no RegisterAction/RegisterFunc was called on it).
        // Dalamud cleans up all ICallGateProviders when the plugin interface is released on unload.
        // Unsubscribing consumers is their responsibility; we have no subscriber handles to release here.
        // The ICallGateProvider<bool, object> interface does not expose an explicit disposal method for
        // send-only gates, so cleanup happens implicitly via plugin unload.

        _log.Info("Daedalus IPC disposed");
    }
}
