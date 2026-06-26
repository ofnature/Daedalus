using Daedalus.Models.Action;

namespace Daedalus.Services.Action;

/// <summary>
/// Level + job-quest availability for actions. Used by rotation schedulers, Data lookup
/// helpers, and modules when resolving upgrade chains below max level.
/// </summary>
public static class ActionAvailability
{
    public static bool MeetsLevel(byte level, ActionDefinition action) => level >= action.MinLevel;

    /// <summary>
    /// True when level is met and the action is learned (or <paramref name="actionService"/> is null for level-only tests).
    /// </summary>
    public static bool MeetsLevelAndLearned(byte level, IActionService? actionService, ActionDefinition action)
    {
        if (level < action.MinLevel)
            return false;

        return actionService is null || actionService.IsActionLearned(action.ActionId);
    }

    /// <summary>Upgrade when learned, otherwise fallback (single tier).</summary>
    public static ActionDefinition Pick(byte level, IActionService? actionService, ActionDefinition upgrade, ActionDefinition fallback)
        => MeetsLevelAndLearned(level, actionService, upgrade) ? upgrade : fallback;

    /// <summary>First entry in <paramref name="tiersHighestFirst"/> that meets level and is learned.</summary>
    public static ActionDefinition FirstAvailable(
        byte level,
        IActionService? actionService,
        ActionDefinition[] tiersHighestFirst,
        ActionDefinition fallback)
    {
        foreach (var action in tiersHighestFirst)
        {
            if (MeetsLevelAndLearned(level, actionService, action))
                return action;
        }

        return fallback;
    }

    /// <summary>First entry in <paramref name="tiersHighestFirst"/> that meets level and is learned, or null.</summary>
    public static ActionDefinition? FirstAvailableOrNull(
        byte level,
        IActionService? actionService,
        ActionDefinition[] tiersHighestFirst)
    {
        foreach (var action in tiersHighestFirst)
        {
            if (MeetsLevelAndLearned(level, actionService, action))
                return action;
        }

        return null;
    }
}
