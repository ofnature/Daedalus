using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace Daedalus.Services.Beastmaster;

/// <summary>
/// "/dae crucible record" — writes down everything a Crucible run shows and sends, so the farming mode can be built
/// from what the game actually does (docs/crucible-farm-plan.md, P0). Off until asked; again to stop.
/// <list type="bullet">
/// <item>Every window that opens or closes, and its values whenever they change (HUD windows, named "_…", skipped).</item>
/// <item>Every click a window sends (<c>AtkUnitBase.FireCallback</c>, the hook Odysseus's recorder uses).</item>
/// <item>The two Crucible agents — the board's stage list (<c>AgentXBMStageDetailList</c>: mode, content, entries)
/// and the team picker (<c>AgentXBMPetParty</c>: mode, selected pets) — and what each receives.</item>
/// <item>The run inventory (<c>InstanceContentCrucibleOfTheUnbroken</c>), territory and combat changes.</item>
/// <item>Where you walk (POS, every 0.75y moved) and the objects around you as they appear and go (OBJ+ / OBJ-) —
/// the board is walked, not clicked, so this is what a route follower would be built from.</item>
/// </list>
/// One file per recording, under the plugin's config folder, <c>crucible-recordings\</c>.
/// </summary>
public sealed unsafe class CrucibleRecorder : IDisposable
{
    private delegate byte FireCallbackDelegate(AtkUnitBase* unit, uint valueCount, AtkValue* values, byte close);
    private delegate AtkValue* AgentReceiveEventDelegate(AgentInterface* agent, AtkValue* returnValue, AtkValue* values, uint valueCount, ulong eventKind);

    private static readonly TimeSpan PollEvery = TimeSpan.FromMilliseconds(250);

    private readonly IGameInteropProvider _interop;
    private readonly string _folder;
    private readonly Action<string> _log;
    private readonly Dictionary<string, int> _openAddons = new(StringComparer.Ordinal);
    private readonly Dictionary<nint, Hook<AgentReceiveEventDelegate>> _agentHooks = new();
    private readonly Dictionary<nint, string> _agentLabels = new();
    private Hook<FireCallbackDelegate>? _callbackHook;
    private StreamWriter? _file;
    private DateTime _nextPollUtc;
    private string _stageState = "";
    private string _partyState = "";
    private string _inventoryState = "";
    private uint _territory;
    private bool _inCombat;
    private System.Numerics.Vector3 _lastPos = new(float.NaN);
    private float _lastHpPct = -1f;
    private string _statusState = "";
    private string _releaseState = "";
    private Dictionary<ulong, string> _objects = new();

    private readonly Daedalus.Services.ICombatEventService? _combat;
    private readonly System.Collections.Concurrent.ConcurrentQueue<(DateTime At, uint Caster, uint Target, uint Action, int Amount, bool Crit)> _actions = new();

    /// <param name="combat">Action results from every caster — logged for you and your familiar as ACT / HIT lines.</param>
    public CrucibleRecorder(
        IGameInteropProvider interop, string configFolder, Action<string> log, Daedalus.Services.ICombatEventService? combat = null)
    {
        _interop = interop;
        _folder = Path.Combine(configFolder, "crucible-recordings");
        _log = log;
        _combat = combat;
    }

    // Hook thread: queue only; Update writes them (with names) on the framework thread.
    private void OnAbilityUsed(uint caster, uint action) => _actions.Enqueue((DateTime.Now, caster, 0, action, -1, false));

    private void OnDamageDealt(Daedalus.Services.DamageDealtEvent e)
        => _actions.Enqueue((DateTime.Now, e.CasterEntityId, e.TargetEntityId, e.ActionId, e.Amount, e.IsCrit));

    /// <summary>
    /// Every action you or your familiar resolve (ACT) and every hit you or it land (HIT), with names — the rotation as
    /// the game saw it. Other casters' lines are dropped.
    /// </summary>
    private void DrainActions(IPlayerCharacter? player, IObjectTable objects)
    {
        if (player == null)
        {
            _actions.Clear();
            return;
        }

        var self = player.EntityId;
        while (_actions.TryDequeue(out var a))
        {
            var caster = objects.SearchByEntityId(a.Caster);
            var mine = a.Caster == self || caster?.OwnerId == self;
            if (!mine)
                continue;

            var who = a.Caster == self ? "YOU" : $"PET {caster?.Name.TextValue}";
            var name = ActionName(a.Action);
            if (a.Amount < 0)
            {
                Write($"ACT {who} {name} ({a.Action})");
                continue;
            }

            var target = objects.SearchByEntityId(a.Target)?.Name.TextValue ?? $"#{a.Target:X}";
            Write($"HIT {who} {name} -> {target} {a.Amount}{(a.Crit ? " crit" : "")}");
        }
    }

    private readonly Dictionary<uint, string> _actionNames = new();

    private string ActionName(uint id)
    {
        if (_actionNames.TryGetValue(id, out var cached))
            return cached;
        var name = ActionNameLookup?.Invoke(id) ?? $"#{id}";
        _actionNames[id] = name;
        return name;
    }

    /// <summary>Action id → name (set by the plugin from the Action sheet).</summary>
    public Func<uint, string>? ActionNameLookup { get; set; }

    public bool Recording => _file != null;

    /// <summary>Start, or stop if already recording. Returns what to tell the player.</summary>
    public string Toggle()
    {
        if (_file != null)
        {
            Stop();
            return "Crucible recording stopped.";
        }

        try
        {
            Directory.CreateDirectory(_folder);
            var path = Path.Combine(_folder, $"crucible-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            _file = new StreamWriter(path, append: false, Encoding.UTF8) { AutoFlush = true };
            _openAddons.Clear();
            _stageState = _partyState = _inventoryState = "";

            _callbackHook ??= _interop.HookFromAddress<FireCallbackDelegate>(
                (nint)AtkUnitBase.Addresses.FireCallback.Value, FireCallbackDetour);
            _callbackHook.Enable();
            HookAgent(AgentId.XBMStageDetailList, "XBMStageDetailList");
            HookAgent(AgentId.XBMPetParty, "XBMPetParty");

            if (_combat != null)
            {
                _combat.OnAbilityUsed += OnAbilityUsed;
                _combat.OnDamageDealt += OnDamageDealt;
            }

            Write("recording started");
            return $"Recording the Crucible to {path}. Play the board, then \"/dae crucible record\" again to stop.";
        }
        catch (Exception ex)
        {
            Stop();
            return $"Crucible recording failed to start: {ex.GetType().Name}: {ex.Message}";
        }
    }

    /// <summary>Framework thread, every frame. Cheap when not recording.</summary>
    public void Update(uint territory, bool inCombat, IPlayerCharacter? player, IObjectTable objects)
    {
        if (_file == null || DateTime.UtcNow < _nextPollUtc)
            return;
        _nextPollUtc = DateTime.UtcNow + PollEvery;

        try
        {
            if (territory != _territory)
            {
                Write($"territory {_territory} -> {territory}");
                _territory = territory;
            }

            if (inCombat != _inCombat)
            {
                Write(inCombat ? "combat started" : "combat ended");
                _inCombat = inCombat;
            }

            PollAddons();
            PollAgents();
            PollInventory();
            PollWorld(player, objects);
        }
        catch (Exception ex)
        {
            Write($"poll failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Your position when it has moved, and every non-player object as it appears or goes.</summary>
    private void PollWorld(IPlayerCharacter? player, IObjectTable objects)
    {
        if (player == null)
            return;

        PollStatuses(player);
        PollReleaseReadiness(player);
        DrainActions(player, objects);

        var pos = player.Position;
        if (float.IsNaN(_lastPos.X) || System.Numerics.Vector3.Distance(pos, _lastPos) >= 0.75f)
        {
            _lastPos = pos;
            Write($"POS {pos.X:F2},{pos.Y:F2},{pos.Z:F2} rot {player.Rotation:F2}");
        }

        var hpPct = player.MaxHp > 0 ? (float)player.CurrentHp / player.MaxHp : 0f;
        if (Math.Abs(hpPct - _lastHpPct) >= 0.05f || (hpPct == 0f && _lastHpPct != 0f))
        {
            _lastHpPct = hpPct;
            Write($"SELF hp {player.CurrentHp}/{player.MaxHp} ({hpPct:P0})");
        }

        // Keyed on what the object IS (kind, id, name, targetable), so a walking enemy is not re-logged every step;
        // the position is written when it appears or changes state.
        var now = new Dictionary<ulong, string>();
        foreach (var obj in objects)
        {
            if (obj.ObjectKind is ObjectKind.Pc || obj.Address == player.Address)
                continue;
            var identity = $"{obj.ObjectKind} base {obj.BaseId} '{obj.Name.TextValue}'{(obj.IsTargetable ? "" : " (untargetable)")}";
            now[obj.GameObjectId] = identity;
            if (!_objects.TryGetValue(obj.GameObjectId, out var old) || old != identity)
            {
                var p = obj.Position;
                Write($"OBJ+ {obj.GameObjectId:X} {identity} at {p.X:F2},{p.Y:F2},{p.Z:F2}");
            }
        }

        foreach (var (id, identity) in _objects)
        {
            if (!now.ContainsKey(id))
                Write($"OBJ- {id:X} {identity}");
        }

        _objects = now;
    }

    /// <summary>
    /// Your buffs as they come and go — One with Nature (4601, from a Battlehorn summon; Tempered Release or Borrow
    /// spends it), the Kinships Borrow grants, Covered (Snarl) — so the recording shows what the rotation could press.
    /// </summary>
    private void PollStatuses(IPlayerCharacter player)
    {
        var ids = player.StatusList.Where(s => s.StatusId != 0).Select(s => s.StatusId).OrderBy(id => id).ToList();
        var state = string.Join(",", ids);
        if (state == _statusState)
            return;

        var before = _statusState.Length == 0 ? [] : _statusState.Split(',').Select(uint.Parse).ToList();
        var gained = ids.Except(before).ToList();
        var lost = before.Except(ids).ToList();
        _statusState = state;
        if (gained.Count > 0 || lost.Count > 0)
            Write($"STATUS +[{string.Join(",", gained)}] -[{string.Join(",", lost)}]{(ids.Contains(4601u) ? " (One with Nature up)" : "")}");
    }

    /// <summary>
    /// What the game answers for Tempered Release (both rows: 44890 self, 47092 hostile) and Borrow on your target —
    /// 0 is usable. The rotation fires them only on 0, and "never used" means it never saw one.
    /// </summary>
    private unsafe void PollReleaseReadiness(IPlayerCharacter player)
    {
        var am = ActionManager.Instance();
        if (am == null)
            return;

        var target = player.TargetObject?.GameObjectId ?? 0xE0000000;
        var self = player.GameObjectId;
        var state = $"44890@target={am->GetActionStatus(ActionType.Action, 44890, target)} "
                    + $"44890@self={am->GetActionStatus(ActionType.Action, 44890, self)} "
                    + $"47092@target={am->GetActionStatus(ActionType.Action, 47092, target)} "
                    + $"borrow={am->GetActionStatus(ActionType.Action, 44895, self)} "
                    + $"adjusted44890={am->GetAdjustedActionId(44890)}";
        if (state == _releaseState)
            return;

        _releaseState = state;
        Write($"RELEASE {state}");
    }

    private void PollAddons()
    {
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = manager->AtkUnitManager.AllLoadedUnitsList;
        for (var i = 0; i < list.Count; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit == null || !unit->IsVisible)
                continue;
            var name = unit->NameString;
            if (string.IsNullOrEmpty(name) || name.StartsWith('_'))
                continue;
            seen.Add(name);

            var values = DescribeValues(unit->AtkValues, unit->AtkValuesCount);
            var hash = values.GetHashCode(StringComparison.Ordinal);
            if (!_openAddons.TryGetValue(name, out var oldHash))
                Write($"OPEN {name} ({unit->AtkValuesCount} values) {values}");
            else if (oldHash != hash)
                Write($"CHANGE {name} {values}");
            _openAddons[name] = hash;
        }

        foreach (var gone in _openAddons.Keys.Where(n => !seen.Contains(n)).ToList())
        {
            _openAddons.Remove(gone);
            Write($"CLOSE {gone}");
        }
    }

    private void PollAgents()
    {
        var module = AgentModule.Instance();
        if (module == null)
            return;

        var stage = (AgentXBMStageDetailList*)module->GetAgentByInternalId(AgentId.XBMStageDetailList);
        if (stage != null)
        {
            var entries = new List<string>();
            for (var i = 0; i < 60; i++)
            {
                var e = stage->Entries[i];
                if (e.EntryType == 0 && e.StageEventId == 0 && e.BattleDetailId == 0)
                    continue;
                entries.Add($"{i}:t{e.EntryType}/ev{e.StageEventId}/et{e.EventType}/st{e.StageEventType}/b{e.BattleDetailId}.{e.BattleDetailIndex}/el{e.ElementId} '{e.Name}'");
            }

            var state = $"open {((AgentInterface*)stage)->IsAgentActive()} mode {stage->Mode} content {stage->ContentId} rank {stage->SelectedRank} "
                        + $"entries [{string.Join(", ", entries)}]";
            if (state != _stageState)
            {
                Write($"AGENT XBMStageDetailList {state}");
                _stageState = state;
            }
        }

        var party = (AgentXBMPetParty*)module->GetAgentByInternalId(AgentId.XBMPetParty);
        if (party != null)
        {
            var selected = string.Join(",", party->SelectedPets.AsSpan().ToArray().Select(p => p.PetId));
            var ids = string.Join(",", party->SelectedPetIds.AsSpan().ToArray().Select(p => p.PetId));
            var candidates = party->CandidatePets.AsSpan().Length;
            var count = Math.Min(party->SelectedPets.AsSpan().Length, 15);
            var hp = string.Join(",", Enumerable.Range(0, count).Select(i => $"{party->PetCurrentHealth[i]}/{party->PetMaxHealth[i]}"));
            var state = $"open {((AgentInterface*)party)->IsAgentActive()} mode {party->Mode}/{party->SubMode} content {party->ContentId} "
                        + $"selected [{selected}] selectedIds [{ids}] hp [{hp}] candidates {candidates} source {party->PetListSource}";
            if (state != _partyState)
            {
                Write($"AGENT XBMPetParty {state}");
                _partyState = state;
            }
        }
    }

    private void PollInventory()
    {
        var framework = EventFramework.Instance();
        if (framework == null)
            return;
        var director = framework->GetInstanceContentDirector();
        if (director == null || director->InstanceContentType != InstanceContentType.CrucibleOfTheUnbroken)
            return;

        var crucible = (InstanceContentCrucibleOfTheUnbroken*)director;
        var items = new List<string>();
        for (var i = 0; i < 10; i++)
            items.Add(crucible->Inventory[i].ItemId.ToString());
        var state = string.Join(",", items);
        if (state != _inventoryState)
        {
            Write($"INVENTORY [{state}]");
            _inventoryState = state;
        }
    }

    private void HookAgent(AgentId id, string label)
    {
        var module = AgentModule.Instance();
        var agent = module == null ? null : module->GetAgentByInternalId(id);
        if (agent == null)
            return;

        var receive = *(nint*)*(nint*)agent; // AtkEventInterface.ReceiveEvent, vtable slot 0
        _agentLabels[(nint)agent] = label;
        if (_agentHooks.ContainsKey(receive))
        {
            _agentHooks[receive].Enable();
            return;
        }

        Hook<AgentReceiveEventDelegate>? hook = null;
        hook = _interop.HookFromAddress<AgentReceiveEventDelegate>(receive, (a, returnValue, values, count, kind) =>
        {
            try
            {
                if (_file != null && _agentLabels.TryGetValue((nint)a, out var name))
                    Write($"AGENT {name} received kind {kind} {DescribeValues(values, (ushort)count)}");
            }
            catch
            {
                // Recording must never get in the way of the event.
            }

            return hook!.Original(a, returnValue, values, count, kind);
        });
        hook.Enable();
        _agentHooks[receive] = hook;
    }

    private byte FireCallbackDetour(AtkUnitBase* unit, uint valueCount, AtkValue* values, byte close)
    {
        try
        {
            if (_file != null)
            {
                var name = unit == null ? "?" : unit->NameString;
                Write($"CLICK {name} sent {DescribeValues(values, (ushort)Math.Min(valueCount, ushort.MaxValue))}{(close != 0 ? " (closing)" : "")}");
            }
        }
        catch
        {
            // Recording must never get in the way of the click itself.
        }

        return _callbackHook!.Original(unit, valueCount, values, close);
    }

    private static string DescribeValues(AtkValue* values, ushort count)
    {
        if (values == null || count == 0)
            return "[]";
        var parts = new List<string>(Math.Min((int)count, 200));
        for (var i = 0; i < count && i < 200; i++)
            parts.Add($"{i}={Describe(values[i])}");
        return "[" + string.Join(" | ", parts) + (count > 200 ? $" | …{count - 200} more" : "") + "]";
    }

    private static string Describe(AtkValue value) => value.Type switch
    {
        AtkValueType.Int => $"i{value.Int}",
        AtkValueType.UInt => $"u{value.UInt}",
        AtkValueType.Bool => value.Byte != 0 ? "true" : "false",
        AtkValueType.Float => $"f{value.Float}",
        AtkValueType.Undefined => "-",
        _ when value.String.Value != null =>
            "\"" + Dalamud.Memory.MemoryHelper.ReadSeStringNullTerminated((nint)value.String.Value).TextValue.Replace("\n", " / ") + "\"",
        _ => $"{value.Type}(u{value.UInt})",
    };

    private void Write(string line)
    {
        try
        {
            _file?.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
        }
        catch
        {
            // A full disk must not crash the game loop.
        }
    }

    private void Stop()
    {
        if (_combat != null)
        {
            _combat.OnAbilityUsed -= OnAbilityUsed;
            _combat.OnDamageDealt -= OnDamageDealt;
        }

        Write("recording stopped");
        _callbackHook?.Disable();
        foreach (var hook in _agentHooks.Values)
            hook.Disable();
        _agentLabels.Clear();
        _file?.Dispose();
        _file = null;
        _log("Crucible recording stopped.");
    }

    public void Dispose()
    {
        if (_file != null)
            Stop();
        _callbackHook?.Dispose();
        foreach (var hook in _agentHooks.Values)
            hook.Dispose();
        _agentHooks.Clear();
    }
}
