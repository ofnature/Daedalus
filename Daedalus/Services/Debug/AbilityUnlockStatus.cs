namespace Daedalus.Services.Debug;

/// <summary>
/// Unlock status of one ability the current job's rotation expects at the current level.
/// <see cref="Learned"/> == false means the action is level-met but not actually usable — almost
/// always an uncompleted job quest (common when leveling via AutoDuty).
/// </summary>
/// <param name="UnlockQuest">The job quest that unlocks it, when a quest does (null otherwise).</param>
/// <param name="ViaUpgradeName">
/// Set when it counts as learned only because its level-learned upgrade is usable (Gravity → Gravity II at 82)
/// while its own quest is not done — it is locked again when synced below <paramref name="ViaUpgradeLevel"/>.
/// </param>
/// <param name="ViaUpgradeLevel">The upgrade's level.</param>
public readonly record struct AbilityUnlockStatus(
    string Name, byte MinLevel, uint ActionId, bool Learned,
    string? UnlockQuest = null, string? ViaUpgradeName = null, byte ViaUpgradeLevel = 0);
