using System;
using System.Linq;
using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;

namespace Daedalus.Services.Positional.Navigation;

/// <summary>
/// Minerva IPC adapter, answering the same questions <see cref="BossModSafetyService"/> does.
/// Fail-open throughout: an unavailable read is "safe", because a mechanics engine that cannot be
/// reached must not become a rotation that never presses anything.
/// <para>
/// The two engines do NOT expose the same shape, and pretending otherwise would be the bug. BMR
/// answers "is THAT position safe" because it pathfinds; Minerva answers "can I stand HERE and
/// cast for N seconds" from geometry, and does its own dodging. Where the mapping is exact this
/// uses it; where it is not, this says so rather than inventing an answer — see each member.
/// </para>
/// </summary>
public sealed class MinervaSafetyService : IBossModSafetyService
{
    private const string PluginInternalName = "Minerva";

    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly IPluginLog? _log;

    private ICallGateSubscriber<bool>? _mustNotMove;
    private ICallGateSubscriber<float>? _maxCastTime;
    private ICallGateSubscriber<float>? _secondsUntilMustNotAct;
    private ICallGateSubscriber<float>? _secondsUntilMustNotMove;

    private float _snapshotMustNotMoveIn = float.MaxValue;

    public MinervaSafetyService(IDalamudPluginInterface pluginInterface, IPluginLog? log = null)
    {
        _pluginInterface = pluginInterface;
        _log = log;
    }

    public bool IsAvailable => IsPluginLoaded(PluginInternalName);

    /// <summary>
    /// Minerva has no "next damage" gate. The nearest honest equivalent is the lead time before
    /// acting is punished, which is what the rotation actually does with this number.
    /// </summary>
    public float NextDamageInSeconds => ReadSeconds(EnsureSecondsUntilMustNotAct());

    /// <summary>Lead time before movement is punished — Minerva's analogue of a zone activating.</summary>
    public float ForbiddenZoneActivationInSeconds => ReadSeconds(EnsureSecondsUntilMustNotMove());

    /// <summary>
    /// Minerva does not publish a zone COUNT, only whether movement is currently punished. One is
    /// enough for every consumer here — they all ask "are there any", never how many.
    /// </summary>
    public int ForbiddenZonesCount => ReadMustNotMove() ? 1 : 0;

    /// <summary>
    /// Minerva exposes no navigation-state gate, and does not need one: it steers itself and
    /// Daedalus never has to yield a path to it the way it yields to BMR's AI.
    /// </summary>
    public bool IsBmrNavigating => false;

    /// <summary>Observability only, and Minerva publishes no equivalent.</summary>
    public Vector3? BmrNaviTarget => null;

    public void BeginUpdateSnapshot()
        => _snapshotMustNotMoveIn = ForbiddenZoneActivationInSeconds;

    /// <summary>
    /// A telegraph appeared since the snapshot: the time until movement is punished has jumped
    /// DOWN, meaning something new is landing sooner than it was a tick ago.
    /// </summary>
    public bool ShouldAbortMovement()
    {
        if (!IsAvailable)
            return false;

        var now = ForbiddenZoneActivationInSeconds;
        return now < _snapshotMustNotMoveIn - PositionalMovementConstants.TelegraphAbortEpsilonSeconds;
    }

    /// <summary>
    /// "Can I stand here for <paramref name="imminentWindowSeconds"/>?" — which is exactly what
    /// <c>Minerva.MaxCastTime</c> answers, in the same units, for the spot the player occupies.
    /// <para>
    /// IMPORTANT and deliberate: Minerva measures the CURRENT position, not an arbitrary one, so
    /// this can only be exact when the destination is where the player already stands — which is
    /// the case that matters, since every caller that gates a hard cast asks about standing
    /// still. For a genuine travel destination it reports Safe rather than guessing, because
    /// Minerva is the one doing the dodging: second-guessing its pathing from here would produce
    /// exactly the two-engines-fighting behaviour the setting exists to prevent.
    /// </para>
    /// </summary>
    public PositionSafety QueryPositionSafety(
        Vector3 destination,
        float imminentWindowSeconds = PositionalMovementConstants.DefaultImminentWindowSeconds)
    {
        if (!IsAvailable)
            return PositionSafety.Safe;

        var castable = ReadSeconds(EnsureMaxCastTime());
        if (castable >= imminentWindowSeconds)
            return PositionSafety.Safe;

        // Standing here is already punished, versus merely about to be.
        return ReadMustNotMove() ? PositionSafety.Unsafe : PositionSafety.Imminent;
    }

    /// <summary>
    /// Minerva publishes no segment test. Reported safe rather than refused: Minerva owns
    /// movement when it is selected, so a dash it did not veto is not Daedalus's to veto either.
    /// </summary>
    public bool IsSegmentSafe(Vector3 from, Vector3 to) => true;

    private bool ReadMustNotMove()
    {
        if (!IsAvailable)
            return false;

        try
        {
            return (_mustNotMove ??= _pluginInterface
                .GetIpcSubscriber<bool>("Minerva.MustNotMove")).InvokeFunc();
        }
        catch
        {
            return false;
        }
    }

    private ICallGateSubscriber<float>? EnsureMaxCastTime()
        => Ensure(ref _maxCastTime, "Minerva.MaxCastTime");

    private ICallGateSubscriber<float>? EnsureSecondsUntilMustNotAct()
        => Ensure(ref _secondsUntilMustNotAct, "Minerva.SecondsUntilMustNotAct");

    private ICallGateSubscriber<float>? EnsureSecondsUntilMustNotMove()
        => Ensure(ref _secondsUntilMustNotMove, "Minerva.SecondsUntilMustNotMove");

    private ICallGateSubscriber<float>? Ensure(ref ICallGateSubscriber<float>? slot, string name)
    {
        if (!IsAvailable)
            return null;

        try
        {
            return slot ??= _pluginInterface.GetIpcSubscriber<float>(name);
        }
        catch (Exception ex)
        {
            _log?.Debug(ex, "Minerva: could not subscribe to {Gate}", name);
            return null;
        }
    }

    /// <summary>
    /// Minerva reports "nothing pending" as NaN and "no limit" as float.MaxValue. Both mean the
    /// same thing to every caller here — no constraint — so both become MaxValue.
    /// </summary>
    private float ReadSeconds(ICallGateSubscriber<float>? gate)
    {
        if (gate is null)
            return float.MaxValue;

        try
        {
            var v = gate.InvokeFunc();
            return float.IsNaN(v) ? float.MaxValue : v;
        }
        catch
        {
            return float.MaxValue;
        }
    }

    private bool IsPluginLoaded(string internalName)
    {
        return _pluginInterface.InstalledPlugins.Any(p =>
            (p.InternalName.Equals(internalName, StringComparison.OrdinalIgnoreCase)
             || p.Name.Equals(internalName, StringComparison.OrdinalIgnoreCase))
            && p.IsLoaded);
    }
}
