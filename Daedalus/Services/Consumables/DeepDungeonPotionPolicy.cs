using Daedalus.Data;

namespace Daedalus.Services.Consumables;

/// <summary>Everything the deep-dungeon potion decision needs, gathered live.</summary>
/// <param name="DungeonId">InstanceContentDeepDungeon.DeepDungeonId; 0 outside a deep dungeon.</param>
/// <param name="Regenerating">A Rehabilitation (or Anointed) is already on the character.</param>
/// <param name="ItemsBlocked">The Item Penalty floor effect is up.</param>
/// <param name="RaiseImmune">Transcendent is up: just raised, immune to damage until something is used.</param>
/// <param name="Held">How many of this dungeon's potion are in the bag.</param>
/// <param name="SecondsSinceAttempt">Since the last press, confirmed or not.</param>
/// <param name="UnconfirmedAttempts">Presses in a row that no regen followed.</param>
public readonly record struct DeepDungeonPotionSituation(
    bool Enabled,
    byte DungeonId,
    bool Alive,
    bool InCombat,
    uint CurrentHp,
    uint MaxHp,
    float HpThreshold,
    bool Regenerating,
    bool ItemsBlocked,
    bool RaiseImmune,
    uint Held,
    bool IsCasting,
    double SecondsSinceAttempt,
    int UnconfirmedAttempts);

/// <param name="Wanted">HP is low enough to drink; when <paramref name="Fire"/> is false, Reason is what holds it.</param>
public readonly record struct DeepDungeonPotionDecision(bool Fire, bool Wanted, DeepDungeonPotion Potion, string Reason);

/// <summary>
/// Drinks the deep dungeon's own regen potion when HP is low in a fight. The gates are BossMod's
/// (Autorotation/Standard/xan/AI/DeepDungeon.cs): in combat, no regen running, no Item Penalty.
/// </summary>
public static class DeepDungeonPotionPolicy
{
    /// <summary>Between presses. The item's animation lock is about a second, and the regen takes a moment to show.</summary>
    public const float RetrySeconds = 3f;

    /// <summary>This many presses with no regen to show for them, and the game is refusing: wait it out.</summary>
    public const int AttemptsBeforeBackoff = 3;

    public const float BackoffSeconds = 30f;

    public const string JustPressed = "just pressed";
    public const string MidCast = "mid-cast";

    public static DeepDungeonPotionDecision Decide(in DeepDungeonPotionSituation s)
    {
        if (!s.Enabled)
            return Quiet("disabled in settings");
        if (DeepDungeonPotions.ForDungeon(s.DungeonId) is not { } potion)
            return Quiet("not in a deep dungeon");
        if (!s.Alive)
            return Quiet("dead");
        if (!s.InCombat)
            return Quiet("not in combat");
        if (s.MaxHp == 0 || (float)s.CurrentHp / s.MaxHp >= s.HpThreshold)
            return Quiet("HP above the threshold");
        if (s.Regenerating)
            return Quiet("already regenerating");
        if (s.RaiseImmune)
            return Quiet("just raised -- a potion would end the immunity");

        if (s.ItemsBlocked)
            return Held(potion, "Item Penalty -- items cannot be used on this floor");
        if (s.Held == 0)
            return Held(potion, $"no {potion.Name} in the bag");
        if (s.UnconfirmedAttempts >= AttemptsBeforeBackoff && s.SecondsSinceAttempt < BackoffSeconds)
            return Held(potion, $"backing off -- {AttemptsBeforeBackoff} presses and no regen");
        if (s.SecondsSinceAttempt < RetrySeconds)
            return Held(potion, JustPressed);
        if (s.IsCasting)
            return Held(potion, MidCast);

        return new(true, true, potion, potion.Name);
    }

    private static DeepDungeonPotionDecision Quiet(string reason) => new(false, false, default, reason);

    private static DeepDungeonPotionDecision Held(DeepDungeonPotion potion, string reason) => new(false, true, potion, reason);
}
