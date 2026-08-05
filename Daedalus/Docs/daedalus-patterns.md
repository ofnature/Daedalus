---
name: daedalus-patterns
description: Daedalus rotation plugin specific patterns — job state, skill evaluation, cooldown tracking, and multibox coordinator conventions. Update as new patterns emerge.
sources: [chat]
---

# Daedalus Rotation Plugin Patterns

## Core Architecture Principle

Daedalus reads game state and recommends/queues actions — it never submits actions to the server automatically without user input. All rotation logic is evaluation only.

## Job State Pattern

Each job module reads its own state from game memory via FFXIVClientStructs. State should be fetched fresh each evaluation cycle, not cached across frames:

```csharp
private unsafe JobState GetCurrentState()
{
    var player = (Character*)ClientState.LocalPlayer?.Address;
    if (player == null) return JobState.Empty;

    return new JobState
    {
        CurrentJob = (Job)player->CharacterData.ClassJob,
        CurrentHp = player->Health,
        MaxHp = player->MaxHealth,
        CurrentMp = player->Mana,
        // etc.
    };
}
```

## Skill/Action Evaluation

Rotation logic returns a recommended action, it does not execute it:

```csharp
public uint? EvaluateNextAction(JobState state)
{
    // Return action ID to suggest, or null if nothing to suggest
    if (state.ResourceGauge >= 80 && CanUseSkill(state, SkillId.BigSkill))
        return (uint)SkillId.BigSkill;

    return null;
}
```

## Cooldown Tracking

Use Dalamud's `IActionManager` or read directly from game structs — never track cooldowns manually with timers:

```csharp
// Check if an action is available via game's own cooldown system
private bool IsActionReady(uint actionId)
{
    // Use ActionManager from FFXIVClientStructs
    unsafe
    {
        return ActionManager.Instance()->GetActionStatus(ActionType.Action, actionId) == 0;
    }
}
```

## Buff/Debuff Checking

Read status effects from the game's status array:

```csharp
private unsafe bool HasBuff(uint statusId)
{
    var player = (BattleChara*)ClientState.LocalPlayer?.Address;
    if (player == null) return false;

    var statusList = player->GetStatusManager();
    for (var i = 0; i < statusList->NumValidStatuses; i++)
    {
        if (statusList->Status[i].StatusId == statusId)
            return true;
    }
    return false;
}
```

## Job Module Structure

Each job should be its own class implementing a shared interface:

```csharp
public interface IJobModule
{
    Job SupportedJob { get; }
    uint? EvaluateNextAction(JobState state);
    void Dispose();
}

public class PaladinModule : IJobModule
{
    public Job SupportedJob => Job.PLD;

    public uint? EvaluateNextAction(JobState state)
    {
        // Paladin-specific rotation logic
    }

    public void Dispose() { }
}
```

Register modules in a central registry — don't hardcode job checks in the main plugin class.

## LAN Multibox Coordinator

When sending state to other clients on the LAN:
- Only send your own character's state — never read or relay other players' data
- Use a simple local broadcast (UDP or named pipe) — keep it on localhost/LAN only
- Serialize with a fixed-width binary format or JSON — never raw pointers
- Version your protocol so clients at different plugin versions can detect mismatches

## Naming Conventions (Match Existing Daedalus Code)

Check the existing codebase conventions before adding new code. Common patterns in Dalamud rotation plugins:
- Job enum values match FFXIV's internal job IDs
- Action/skill IDs are constants, not magic numbers
- State structs are immutable value types where possible

## Safety Rules for Rotation Plugins

- Never call `ActionManager.UseAction` automatically — display suggestions only
- Never read other party members' cooldowns through unsupported means
- All buff/debuff checks use status IDs, not names (names change with localization)
- Validate that the local player is in combat/loaded before any evaluation

## PR Lessons Learned

Add entries here as issues are found:
- Always null-check LocalPlayer — it's null in lobby, character select, loading screens
- Status IDs not status names for buff checks — names are localized
- Don't cache game struct pointers across frames — re-fetch each evaluation
