using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using Moq;
using Daedalus.Data;
using Daedalus.Models.Action;
using Daedalus.Rotation.ArtemisCore.Context;
using Daedalus.Rotation.ArtemisCore.Helpers;
using Daedalus.Rotation.ArtemisCore.Modules;
using Daedalus.Rotation.Common.Scheduling;
using Daedalus.Services.Action;
using Daedalus.Services.Targeting;
using Daedalus.Tests.Mocks;

namespace Daedalus.Tests.Sim.Artemis;

/// <summary>One fight for the simulator: the level, how hard a point of potency hits there, and when the boss can be hit.</summary>
public sealed record SimFight
{
    public string Name { get; init; } = "striking dummy";

    /// <summary>The level the fight syncs to: 30 on the First Board, 40 on the Second, 50 from the Third.</summary>
    public byte Level { get; init; } = 30;

    /// <summary>Damage one point of the beastmaster's potency does here (per board, from the recordings).</summary>
    public double DamagePerPotency { get; init; } = 0.63;

    /// <summary>
    /// What a point of the familiar's potency is worth in the beastmaster's. The familiar's plain attack does 127-148
    /// of the beastmaster's potency points on every board (2,981 recorded fights), so one factor serves them all.
    /// </summary>
    public double PetPotencyFactor { get; init; } = 1.47;

    public double PetDamagePerPotency => DamagePerPotency * PetPotencyFactor;

    public double Duration { get; init; } = 120;

    /// <summary>Seconds the beastmaster is out of melee range (dodging), as [from, to).</summary>
    public IReadOnlyList<(double From, double To)> OutOfRange { get; init; } = [];

    /// <summary>Seconds the boss cannot be targeted, as [from, to).</summary>
    public IReadOnlyList<(double From, double To)> Untargetable { get; init; } = [];

    /// <summary>The beasts on the horns, first out. Empty: no familiar.</summary>
    public IReadOnlyList<BstFamiliar> Roster { get; init; } = [];

    public double FamiliarAutoSeconds { get; init; } = 3.03;
    public double FamiliarAutoPotency { get; init; } = 95;

    /// <summary>Seconds before a horn whose familiar retreated can be blown again. Not measured: an assumption.</summary>
    public double HornCooldown { get; init; } = 60;

    /// <summary>Changes to the default configuration (every Beastmaster toggle at its shipped default).</summary>
    public Action<Configuration>? Configure { get; init; }
}

public readonly record struct SimEvent(double T, string Who, string Name, double Damage, string Note);

public sealed class SimResult
{
    public required SimFight Fight { get; init; }
    public List<SimEvent> Events { get; } = [];
    public double TotalDamage => Events.Sum(e => e.Damage);
    public double Dps => TotalDamage / Fight.Duration;
    public int ChainPresses => Events.Count(e => e.Who == "me" && e.Name is "Smash Axe" or "Axeblade Bite" or "Shieldsplitter");
    public int Count(string name) => Events.Count(e => e.Name == name);
    public IEnumerable<SimEvent> Presses(string name) => Events.Where(e => e.Name == name);

    /// <summary>TP thrown away because the gauge was full when more came in.</summary>
    public int TpOvercapped { get; set; }

    /// <summary>What the instinct picker said it was doing, each time that changed.</summary>
    public List<(double T, string State)> InstinctStates { get; } = [];

    /// <summary>Seconds the chain's GCD sat ready with the boss in reach.</summary>
    public double IdleInRange { get; set; }
}

/// <summary>
/// Runs the real Beastmaster rotation (<see cref="DamageModule"/> through the real <see cref="RotationScheduler"/>)
/// against a model of the game, on a clock that is not the wall's.
/// <para>
/// The model's rules are the tooltips' (Action sheet, 2026-11-15) and three fits from 677 recorded fights
/// (<c>burn-reference/bst-sandbox/fit.py</c>): TP comes only from the chain's combo hits (13 and 15), an intentional
/// combo's follow-up is 1.5x the axe that completed it and lands 2.1 s later, and the familiar gains 10 TP an attack.
/// Marked ASSUMED where neither says: an instinctual combo's follow-up (about 1,100 potency, 21 samples), Universality
/// (about 4,000, 22 samples), the horn cooldown, and what a familiar's potency is worth.
/// </para>
/// </summary>
public sealed class ArtemisSim
{
    public const ulong PlayerId = 0x10000001, TargetId = 0x40000001, FamiliarId = 0x40000002;
    private const uint AxeRecastKey = 44884; // the four axes share cooldown group 16
    private const double Tick = 0.05;

    /// <summary>The chain's recast as the recordings have it: 2.5 s on the sheet, 2.55 s between presses.</summary>
    private const double Gcd = 2.55;

    /// <summary>An off-GCD press lands 0.75 s after the GCD before it in the recordings (lock, less the 0.1 s weave buffer).</summary>
    private const double AnimationLock = 0.85;
    private const int MaxTp = ArtemisGauge.MaxTp, MinTp = ArtemisGauge.MinSpendTp;
    private const uint BrutalRage = 44930, HawkishTalons = 44931, RisenFall = 44932, Calamity = 44933;
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly SimFight _f;
    private readonly SimResult _result;
    private readonly Configuration _config;
    private readonly DamageModule _module = new();
    private readonly ArtemisInstinctTracker _instinct = new();
    private readonly ArtemisTrickPairing _pairing = new();
    private readonly ArtemisDebugState _debug = new();
    private readonly Dictionary<uint, double> _readyAt = [];
    private readonly List<(double At, Action Run)> _pending = [];
    private readonly double[] _hornReadyAt;

    private double _t, _gcdReadyAt, _animationLockUntil, _comboUntil, _litUntil, _nextPetAuto, _nextAuto, _resummonAt = -1;
    private int _combo, _tp, _petTp, _petTpAtLastUse, _mastered, _natural, _chain, _horn, _shieldCharges;
    private double _shieldChargeAt = double.MaxValue;
    private InstinctAffinity _lit;
    private bool _familiarOut, _oneWithNature;
    private int _ogcdsThisGcd;
    private double _lastActionAt = -10;
    private uint _lastOgcd, _lastGcd;

    public ArtemisSim(SimFight fight)
    {
        _f = fight;
        _result = new SimResult { Fight = fight };
        _config = MockBuilders.CreateDefaultConfiguration();
        _config.Targeting.SuppressDamageOnForcedMovement = false;
        fight.Configure?.Invoke(_config);
        _module.UtcNow = () => Now;
        _instinct.UtcNow = () => Now;
        _pairing.UtcNow = () => Now;
        _hornReadyAt = new double[Math.Max(1, fight.Roster.Count)];
        _familiarOut = _oneWithNature = fight.Roster.Count > 0;
        _nextPetAuto = 1.5;
        _nextAuto = 0.2;
        _shieldCharges = fight.Level >= 36 ? 3 : 1;
    }

    private DateTime Now => Epoch.AddSeconds(_t);
    private bool Targetable => !_f.Untargetable.Any(w => _t >= w.From && _t < w.To);
    private bool InRange => !_f.OutOfRange.Any(w => _t >= w.From && _t < w.To);
    private double Edge => InRange ? 0.5 : 9.5;
    private BstFamiliar? Familiar => _familiarOut ? _f.Roster[_horn] : null;
    private InstinctAffinity Lit => _t < _litUntil ? _lit : InstinctAffinity.None;
    private bool Ready(uint key) => !_readyAt.TryGetValue(key, out var at) || _t >= at;

    public static SimResult Run(SimFight fight) => new ArtemisSim(fight).Run();

    public SimResult Run()
    {
        var actions = BuildActionService();
        var context = BuildContext(actions);
        var scheduler = new RotationScheduler(actions.Object, new Mock<IJobGauges>().Object, _config);

        for (var tick = 0; tick * Tick < _f.Duration; tick++)
        {
            _t = Math.Round(tick * Tick, 2);
            World();

            scheduler.Reset();
            _module.CollectCandidates(context, scheduler, isMoving: !InRange);
            // The GCD first: in the game it is queued in the last half second of the one before, so it goes out the
            // instant that one ends and an off-GCD press can never take its place.
            if (_t >= _gcdReadyAt && _t >= _animationLockUntil)
            {
                if (!scheduler.DispatchGcd(context).Dispatched && Targetable && InRange)
                    _result.IdleInRange += Tick;
            }
            else if (_t >= _gcdReadyAt && Targetable && InRange)
            {
                _result.IdleInRange += Tick; // an off-GCD press's lock is holding the GCD back
            }

            if (CanWeave())
                scheduler.DispatchOgcd(context);

            var state = _debug.InstinctState ?? "";
            if (_result.InstinctStates.Count == 0 || _result.InstinctStates[^1].State != state)
                _result.InstinctStates.Add((_t, state));
        }

        return _result;
    }

    // ── the world: what happens whether or not a button is pressed ──────────────────────────────────

    private void World()
    {
        for (var i = _pending.Count - 1; i >= 0; i--)
        {
            if (_t < _pending[i].At)
                continue;
            var run = _pending[i].Run;
            _pending.RemoveAt(i);
            run();
        }

        if (_combo != 0 && _t >= _comboUntil)
            _combo = 0;
        if (_lit != InstinctAffinity.None && _t >= _litUntil)
        {
            _lit = InstinctAffinity.None;
            _chain = 0;
        }

        if (!_familiarOut && _resummonAt >= 0 && _t >= _resummonAt)
        {
            var next = Enumerable.Range(1, _f.Roster.Count).Select(k => (_horn + k) % _f.Roster.Count)
                .Cast<int?>().FirstOrDefault(k => _t >= _hornReadyAt[k!.Value]);
            if (next is { } horn)
            {
                _horn = horn;
                _familiarOut = _oneWithNature = true; // a summon grants One with Nature
                _resummonAt = -1;
                _nextPetAuto = _t + 1.5;
                Log("me", $"Battlehorn ({_f.Roster[horn].Name})", 0, "");
            }
        }

        if (_familiarOut && Targetable && _t >= _nextPetAuto)
        {
            _nextPetAuto = _t + _f.FamiliarAutoSeconds;
            GainPetTp(10);
            Log("pet", "attack", _f.FamiliarAutoPotency * _f.PetDamagePerPotency, "");
        }

        if (Targetable && InRange && _t >= _nextAuto)
        {
            _nextAuto = _t + 2.5;
            Log("me", "Auto-attack", 75 * _f.DamagePerPotency, ""); // 71-79 potency on every board
        }

        if (_shieldCharges < (_f.Level >= 36 ? 3 : 1) && _t >= _shieldChargeAt)
        {
            _shieldCharges++;
            _shieldChargeAt = _shieldCharges < (_f.Level >= 36 ? 3 : 1) ? _t + 60 : double.MaxValue;
        }
    }

    private void GainTp(int amount)
    {
        var after = _tp + amount;
        if (after > MaxTp)
            _result.TpOvercapped += after - MaxTp;
        _tp = Math.Min(MaxTp, after);
    }

    private void GainPetTp(int amount) => _petTp = Math.Min(MaxTp, _petTp + amount);

    private void Log(string who, string name, double damage, string note)
        => _result.Events.Add(new SimEvent(Math.Round(_t, 2), who, name, Math.Round(damage, 1), note));

    // ── what the game answers when asked whether an action can be used ──────────────────────────────

    private uint Status(uint id)
    {
        const uint Ok = 0, OutOfRange = 562, CannotUseYet = 572, Recasting = 582;
        if (id is 44879 or 44883 or 44885)
            return !Targetable ? CannotUseYet : Edge > 3 ? OutOfRange : Ok;

        if (BSTActions.AffinityOf(id) != InstinctAffinity.None)
        {
            if (!Targetable) return CannotUseYet;
            if (Edge > 3) return OutOfRange;
            if (_tp < MinTp) return CannotUseYet;
            return Ready(AxeRecastKey) ? Ok : Recasting;
        }

        if (id == BSTActions.Trick.ActionId)
            return _familiarOut && _petTp >= MinTp && Targetable && Ready(id) ? Ok : CannotUseYet;
        if (id == BSTActions.PartingBlow.ActionId)
            return _familiarOut && Targetable && Ready(id) ? Ok : CannotUseYet;
        if (id == BSTActions.TemperedRelease.ActionId)
            return _familiarOut && _oneWithNature && Ready(id) ? Ok : CannotUseYet; // once a summon
        if (id == BSTActions.Rally.ActionId || id == BSTActions.RallyingCheer.ActionId)
            return Ready(id) ? Ok : Recasting;
        if (id == BSTActions.ShieldCharge.ActionId)
            return _shieldCharges > 0 && Targetable ? Ok : CannotUseYet;
        return CannotUseYet; // Borrow, Beast Mode, the duty actions: not modelled
    }

    /// <summary>Level 50 with a full gauge: the axes become their upgrades.</summary>
    private uint Adjusted(uint id) => _f.Level < 50 || _tp < MaxTp ? id : id switch
    {
        44884 => BrutalRage,
        44887 => HawkishTalons,
        44888 => RisenFall,
        44889 => Calamity,
        _ => id,
    };

    /// <summary>
    /// ActionService.IsInWeaveWindow, ported: one off-GCD press fits a 2.5 s GCD (each takes about 0.7 s and the last
    /// 0.5 s is kept for the next GCD), more only once the GCD has stalled for 1.5 s.
    /// </summary>
    private bool CanWeave()
    {
        if (_animationLockUntil - _t >= 0.1)
            return false;
        // The chain's next GCD was queued in the last half second of this one and goes out now: no slot beside it.
        if (_t >= _gcdReadyAt && Targetable && InRange)
            return false;
        var gcdRemaining = Math.Max(0, _gcdReadyAt - _t);
        var slots = gcdRemaining <= 0 && _t - _lastActionAt >= 1.5
            ? _ogcdsThisGcd + 1
            : Math.Clamp((int)((gcdRemaining - 0.5 - 0.1) / 0.7), 0, 2);
        return slots > _ogcdsThisGcd && !(gcdRemaining > 0 && gcdRemaining <= 0.5);
    }

    // ── what pressing a button does ─────────────────────────────────────────────────────────────────

    private bool Press(ActionDefinition action, bool gcd)
    {
        var id = action.ActionId;
        if (Status(id) != 0 || (gcd ? _t < _gcdReadyAt || _t < _animationLockUntil : !CanWeave()))
            return false;

        var dpp = _f.DamagePerPotency;
        switch (id)
        {
            case 44879:
                Log("me", action.Name, BSTActions.ById(id)!.Value.PotencyAt(_f.Level) * dpp, "");
                _combo = 1;
                _comboUntil = _t + 30;
                break;
            case 44883:
                var bite = _combo == 1;
                Log("me", action.Name, (bite ? 500 : 260) * dpp, bite ? "+13 TP" : "no combo");
                if (bite) GainTp(13);
                _combo = bite ? 2 : 0;
                break;
            case 44885:
                var split = _combo == 2;
                Log("me", action.Name, (split ? 600 : 280) * dpp, split ? "+15 TP" : "no combo");
                if (split) GainTp(15);
                _combo = 0;
                break;
            case 44884 or 44887 or 44888 or 44889:
                PressAxe(action);
                break;
            default:
                PressOther(action);
                break;
        }

        _animationLockUntil = _t + AnimationLock;
        _lastActionAt = _t;
        if (gcd)
        {
            _gcdReadyAt = _t + Gcd;
            _lastGcd = id;
            _ogcdsThisGcd = 0;
        }
        else
        {
            _lastOgcd = id;
            _ogcdsThisGcd++;
        }

        return true;
    }

    private void PressAxe(ActionDefinition action)
    {
        var affinity = BSTActions.AffinityOf(action.ActionId);
        var upgraded = Adjusted(action.ActionId) != action.ActionId;
        var spent = _tp;
        _tp = 0;
        _readyAt[AxeRecastKey] = _t + 5;

        if (upgraded)
        {
            // 1,200 potency, lights its half; on the other half it completes Universality.
            var half = action.ActionId is 44884 or 44888 ? InstinctAffinity.Sunstrider : InstinctAffinity.Moonstalker;
            var name = Adjusted(action.ActionId) switch { BrutalRage => "Brutal Rage", HawkishTalons => "Hawkish Talons", RisenFall => "Risen Fall", _ => "Calamity" };
            Log("me", name, 1200 * _f.DamagePerPotency, $"{spent} TP");
            var lit = Lit;
            if ((lit == InstinctAffinity.Sunstrider || lit == InstinctAffinity.Moonstalker) && lit != half)
            {
                var hit = 4000 * _f.DamagePerPotency; // ASSUMED: 22 samples, median 3,975
                _pending.Add((_t + 3.2, () => Log("me", "Infinitive Combo: Universality", hit, "")));
                Credit(byPlayer: true);
                _lit = InstinctAffinity.None;
                _chain = 0;
                _litUntil = 0;
            }
            else
            {
                _lit = half;
                _chain++;
                _litUntil = _t + 7;
            }

            return;
        }

        var damage = 4.0 * spent * _f.DamagePerPotency; // 400 at 100 TP, 1,000 at 250
        Log("me", action.Name, damage, $"{spent} TP");
        Link(affinity, byPlayer: true, damage, _f.DamagePerPotency);
    }

    /// <summary>One link of the instinct chain: opens it, or completes an intentional or an instinctual combo.</summary>
    private void Link(InstinctAffinity affinity, bool byPlayer, double damage, double dpp)
    {
        var lit = Lit;
        var heart = lit is InstinctAffinity.Volant or InstinctAffinity.Rampant or InstinctAffinity.Durant or InstinctAffinity.Eldritch;
        if (lit == InstinctAffinity.None)
        {
            _lit = affinity;
            _chain = 1;
        }
        else if (heart && affinity == BSTActions.NextInCycle(lit))
        {
            // Rampant or Eldritch completes Sunstrider, Durant or Volant Moonstalker (the axes' tooltips).
            var half = affinity is InstinctAffinity.Rampant or InstinctAffinity.Eldritch
                ? InstinctAffinity.Sunstrider : InstinctAffinity.Moonstalker;
            var hit = damage * 1.5;
            _pending.Add((_t + 2.1, () => Log("me", $"Intentional Combo: {half}", hit, "")));
            Credit(byPlayer);
            _lit = half;
            _chain++;
        }
        else
        {
            var hit = 1100 * dpp; // ASSUMED: 21 samples, about 1,100 potency whatever the axe did
            _pending.Add((_t + 1.2, () => Log("me", $"Instinctual Combo: {affinity}", hit, "")));
            Credit(byPlayer);
            _lit = affinity;
            _chain++;
        }

        _litUntil = _t + 7;
    }

    /// <summary>Whoever's skill finishes a combo banks a charge: Mastered Instinct for the beastmaster, Natural for the familiar.</summary>
    private void Credit(bool byPlayer)
    {
        if (byPlayer && _f.Level >= BSTActions.Rally.MinLevel)
            _mastered = Math.Min(3, _mastered + 1);
        else if (!byPlayer && _f.Level >= BSTActions.RallyingCheer.MinLevel)
            _natural = Math.Min(3, _natural + 1);
    }

    private void PressOther(ActionDefinition action)
    {
        var id = action.ActionId;
        if (id == BSTActions.Trick.ActionId)
        {
            var beast = Familiar!;
            var damage = 5.0 * _petTp * _f.PetDamagePerPotency; // 500 at 100 familiar TP, 1,250 at 250
            Log("me", action.Name, 0, $"{beast.Name}, {_petTp} familiar TP");
            Log("pet", $"{beast.TrickAffinity} skill", damage, "");
            _petTpAtLastUse = _petTp;
            _petTp = 0;
            _readyAt[id] = _t + 3;
            Link(beast.TrickAffinity, byPlayer: false, damage, _f.PetDamagePerPotency);
            _pending.Add((_t + 0.1, _pairing.OnFamiliarAction));
        }
        else if (id == BSTActions.PartingBlow.ActionId)
        {
            Log("me", action.Name, 0, Familiar!.Name);
            Log("pet", "Aetheric Burst", 1000 * _f.PetDamagePerPotency, "");
            GainPetTp(BSTActions.PartingBlowFamiliarTpGain);
            _readyAt[id] = _t + 10;
            _hornReadyAt[_horn] = _t + _f.HornCooldown;
            _familiarOut = false;
            _resummonAt = _t + 2.5;
        }
        else if (id == BSTActions.TemperedRelease.ActionId)
        {
            Log("me", action.Name, 0, Familiar!.Name);
            _readyAt[id] = _t + 30;
            _oneWithNature = false;
        }
        else if (id == BSTActions.Rally.ActionId)
        {
            var gain = BSTActions.RallyBaseTpGain + BSTActions.InstinctStackTpGain * _mastered;
            Log("me", action.Name, 0, $"+{gain} TP ({_mastered} Mastered Instinct)");
            GainTp(gain);
            _mastered = 0;
            _readyAt[id] = _t + 120;
        }
        else if (id == BSTActions.RallyingCheer.ActionId)
        {
            var gain = BSTActions.RallyingCheerBaseTpGain + BSTActions.InstinctStackTpGain * _natural;
            Log("me", action.Name, 0, $"+{gain} familiar TP ({_natural} Natural Instinct)");
            GainPetTp(gain);
            _natural = 0;
            _readyAt[id] = _t + 120;
        }
        else if (id == BSTActions.ShieldCharge.ActionId)
        {
            Log("me", action.Name, BSTActions.ById(id)!.Value.PotencyAt(_f.Level) * _f.DamagePerPotency, "");
            if (_shieldCharges == (_f.Level >= 36 ? 3 : 1))
                _shieldChargeAt = _t + 60;
            _shieldCharges--;
        }
    }

    // ── the fakes the rotation reads the game through ───────────────────────────────────────────────

    private Mock<IActionService> BuildActionService()
    {
        var mock = MockBuilders.CreateMockActionService();
        mock.Setup(x => x.CanExecuteGcd).Returns(() => _t >= _gcdReadyAt && _t >= _animationLockUntil);
        mock.Setup(x => x.CanExecuteOgcd).Returns(CanWeave);
        mock.Setup(x => x.GcdRemaining).Returns(() => (float)Math.Max(0, _gcdReadyAt - _t));
        mock.Setup(x => x.AnimationLockRemaining).Returns(() => (float)Math.Max(0, _animationLockUntil - _t));
        mock.Setup(x => x.GetActionStatusCode(It.IsAny<uint>())).Returns<uint>(Status);
        mock.Setup(x => x.GetActionStatusCode(It.IsAny<uint>(), It.IsAny<ulong>())).Returns<uint, ulong>((id, _) => Status(id));
        mock.Setup(x => x.CanExecuteActionId(It.IsAny<uint>())).Returns<uint>(id => Status(id) == 0);
        mock.Setup(x => x.IsActionReady(It.IsAny<uint>())).Returns<uint>(id =>
            id is 44879 or 44883 or 44885 ? _t >= _gcdReadyAt
            : BSTActions.AffinityOf(id) != InstinctAffinity.None ? Ready(AxeRecastKey)
            : id == BSTActions.ShieldCharge.ActionId ? _shieldCharges > 0
            : Ready(id));
        mock.Setup(x => x.GetCooldownRemaining(It.IsAny<uint>())).Returns<uint>(id =>
            _readyAt.TryGetValue(BSTActions.AffinityOf(id) != InstinctAffinity.None ? AxeRecastKey : id, out var at)
                ? (float)Math.Max(0, at - _t) : 0f);
        mock.Setup(x => x.GetCurrentCharges(It.IsAny<uint>())).Returns<uint>(id =>
            id == BSTActions.ShieldCharge.ActionId ? (uint)_shieldCharges : Status(id) == 0 ? 1u : 0u);
        mock.Setup(x => x.GetAdjustedActionId(It.IsAny<uint>())).Returns<uint>(Adjusted);
        mock.Setup(x => x.WasLastOgcd(It.IsAny<uint>())).Returns<uint>(id => _lastOgcd == id);
        mock.Setup(x => x.WasLastGcd(It.IsAny<uint>())).Returns<uint>(id => _lastGcd == id);
        mock.Setup(x => x.LastOgcdId).Returns(() => _lastOgcd);
        mock.Setup(x => x.ExecuteGcd(It.IsAny<ActionDefinition>(), It.IsAny<ulong>()))
            .Returns<ActionDefinition, ulong>((action, _) => Press(action, gcd: true));
        mock.Setup(x => x.ExecuteOgcd(It.IsAny<ActionDefinition>(), It.IsAny<ulong>()))
            .Returns<ActionDefinition, ulong>((action, _) => Press(action, gcd: false));
        mock.Setup(x => x.ExecuteGcdRaw(It.IsAny<ActionDefinition>(), It.IsAny<uint>(), It.IsAny<ulong>())).Returns(false);
        mock.Setup(x => x.ExecuteOgcdRaw(It.IsAny<ActionDefinition>(), It.IsAny<uint>(), It.IsAny<ulong>())).Returns(false);
        return mock;
    }

    private IArtemisContext BuildContext(Mock<IActionService> actions)
    {
        var player = new Mock<IPlayerCharacter>();
        player.Setup(x => x.Level).Returns(_f.Level);
        player.Setup(x => x.GameObjectId).Returns(PlayerId);
        player.Setup(x => x.EntityId).Returns((uint)PlayerId);
        player.Setup(x => x.CurrentHp).Returns(2000u);
        player.Setup(x => x.MaxHp).Returns(2000u);
        player.Setup(x => x.HitboxRadius).Returns(0.5f);
        player.Setup(x => x.Position).Returns(() => new Vector3(0, 0, InRange ? 3f : 12f));

        var target = new Mock<IBattleNpc>();
        target.Setup(x => x.GameObjectId).Returns(TargetId);
        target.Setup(x => x.EntityId).Returns((uint)TargetId);
        target.Setup(x => x.CurrentHp).Returns(1_000_000u);
        target.Setup(x => x.MaxHp).Returns(1_000_000u);
        target.Setup(x => x.HitboxRadius).Returns(2f);
        target.Setup(x => x.Position).Returns(Vector3.Zero);
        target.Setup(x => x.IsTargetable).Returns(() => Targetable);
        target.Setup(x => x.TargetObjectId).Returns(PlayerId);

        // The familiar is an owned pet in the object table (FamiliarLookup), there only while it is out.
        var familiar = new Mock<IBattleNpc>();
        familiar.Setup(x => x.GameObjectId).Returns(FamiliarId);
        familiar.Setup(x => x.EntityId).Returns((uint)FamiliarId);
        familiar.Setup(x => x.OwnerId).Returns((uint)PlayerId);
        familiar.Setup(x => x.BattleNpcKind).Returns(Dalamud.Game.ClientState.Objects.Enums.BattleNpcSubKind.Pet);
        familiar.Setup(x => x.CurrentHp).Returns(1500u);
        familiar.Setup(x => x.MaxHp).Returns(1500u);
        familiar.Setup(x => x.HitboxRadius).Returns(1f);
        familiar.Setup(x => x.Position).Returns(new Vector3(0, 0, -3f));

        List<IGameObject> InTheWorld() => _familiarOut
            ? [player.Object, target.Object, familiar.Object]
            : [player.Object, target.Object];
        var objects = new Mock<IObjectTable>();
        objects.Setup(x => x.SearchById(TargetId)).Returns(target.Object);
        objects.Setup(x => x.SearchById(PlayerId)).Returns(player.Object);
        objects.Setup(x => x.SearchById(FamiliarId)).Returns(() => _familiarOut ? familiar.Object : null);
        objects.Setup(x => x.GetEnumerator()).Returns(() => InTheWorld().GetEnumerator());
        objects.As<System.Collections.IEnumerable>().Setup(x => x.GetEnumerator()).Returns(() => InTheWorld().GetEnumerator());

        var targeting = MockBuilders.CreateMockTargetingService();
        targeting.Setup(x => x.IsDamageTargetingPaused()).Returns(false);
        targeting.Setup(x => x.FindEnemyForAction(It.IsAny<EnemyTargetingStrategy>(), It.IsAny<uint>(), It.IsAny<IPlayerCharacter>()))
            .Returns(() => Targetable ? target.Object : null);
        targeting.Setup(x => x.GetGameHardTargetId()).Returns(() => Targetable ? TargetId : 0);

        var context = new Mock<IArtemisContext>();
        context.Setup(x => x.Player).Returns(player.Object);
        context.Setup(x => x.InCombat).Returns(true);
        context.Setup(x => x.Configuration).Returns(_config);
        context.Setup(x => x.ActionService).Returns(actions.Object);
        context.Setup(x => x.TargetingService).Returns(targeting.Object);
        context.Setup(x => x.ObjectTable).Returns(objects.Object);
        context.Setup(x => x.DebuffDetectionService).Returns(MockBuilders.CreateMockDebuffDetectionService().Object);
        context.Setup(x => x.Instinct).Returns(_instinct);
        context.Setup(x => x.TrickPairing).Returns(_pairing);
        context.Setup(x => x.Debug).Returns(_debug);
        context.Setup(x => x.HasFamiliar).Returns(() => _familiarOut);
        context.Setup(x => x.FamiliarBeast).Returns(() => Familiar);
        context.Setup(x => x.ComboStep).Returns(() => _combo);
        context.Setup(x => x.NoReplacementHorn).Returns(() =>
            !Enumerable.Range(0, _f.Roster.Count).Any(k => k != _horn && _t >= _hornReadyAt[k]));
        context.Setup(x => x.Gauge).Returns(() => new ArtemisGauge(
            _tp, _petTp, _petTpAtLastUse, ActiveBattlehorn: 0, ComboState: 0, Lit, _chain,
            KinshipBattlehorn: 0, KinshipClassification: 0, _natural, _mastered));
        return context.Object;
    }
}
