namespace Daedalus.Rotation.Transform;

/// <summary>The creature a deep-dungeon pomander turned the character into.</summary>
public enum TransformForm
{
    /// <summary>Not transformed.</summary>
    None,

    /// <summary>Transformed into something this table does not know: the job stands down and nothing is pressed.</summary>
    Unknown,
    Manticore,
    Succubus,
    Kuribu,
    Mudball,
    Bomb,
    Dreadnaught,
}

/// <summary>How a form's one action is aimed.</summary>
public enum TransformAim
{
    /// <summary>At the enemy; the game's own range applies.</summary>
    Enemy,

    /// <summary>Placed on the ground under the enemy.</summary>
    Ground,

    /// <summary>Around the character.</summary>
    Self,

    /// <summary>A dash straight ahead: the character is turned to the enemy first.</summary>
    Forward,
}

/// <summary>
/// A form's one action. <paramref name="Reach"/> is how far the enemy may be for the action to hit it (for
/// <see cref="TransformAim.Enemy"/>, the action's own range). Numbers are the game's Action sheet rows.
/// </summary>
public readonly record struct TransformAction(uint ActionId, string Name, TransformAim Aim, float Reach, float CastTime);

/// <summary>What <see cref="TransformPolicy.Reject"/> is told about this frame.</summary>
public readonly record struct TransformFacts(
    bool InCombat, bool EnemyAlive, float DistanceToEnemy, float EnemyHitbox, bool IsMoving, bool Ready);

/// <summary>
/// Transformed by a deep-dungeon pomander (Palace of the Dead's manticore, succubus and kuribu, Eureka Orthos's
/// dreadnaught, Pilgrim's Traverse's bomb and mudball): the job's actions are refused ("Unable to execute command
/// while transformed") and the bar holds one action. The job rotation stands down and that action is pressed.
/// <para>Pilgrim's Traverse, 2026-11-21: a Warrior and a Samurai turned into bombs stood in the pack for the whole
/// transformation with Big Burst unpressed, the rotation logging "stalled -- no castable action".</para>
/// <para>The form table and the reaches are BossMod's (Autorotation/Standard/xan/AI/DeepDungeon.cs).</para>
/// </summary>
public static class TransformPolicy
{
    /// <summary>"Transfiguration": 565 in Palace of the Dead and Eureka Orthos, 4708 in Pilgrim's Traverse.</summary>
    public static bool IsTransformStatus(uint statusId) => statusId is 565 or 4708;

    /// <summary>The form is the low byte of the Transfiguration status's param.</summary>
    public static TransformForm FormOf(ushort statusParam) => (statusParam & 0xFF) switch
    {
        42 => TransformForm.Manticore,
        43 => TransformForm.Succubus,
        49 => TransformForm.Kuribu,
        54 => TransformForm.Mudball,
        55 => TransformForm.Bomb,
        244 => TransformForm.Dreadnaught,
        _ => TransformForm.Unknown,
    };

    /// <summary>The form's one action, or null when the form is not known.</summary>
    public static TransformAction? ActionFor(TransformForm form) => form switch
    {
        TransformForm.Manticore => new(6273, "Pummel", TransformAim.Enemy, 3f, 0f),
        TransformForm.Succubus => new(6274, "Void Fire II", TransformAim.Enemy, 25f, 2.5f),
        TransformForm.Kuribu => new(6871, "Heavenly Judge", TransformAim.Ground, 25f, 2.5f),
        TransformForm.Dreadnaught => new(32781, "Rotosmash", TransformAim.Enemy, 3f, 0f),
        TransformForm.Mudball => new(44628, "Rocky Roll", TransformAim.Forward, 25f, 0f),
        TransformForm.Bomb => new(44629, "Big Burst", TransformAim.Self, 15f, 1.5f),
        _ => null,
    };

    /// <summary>
    /// Why the action is not pressed this frame, or null to press it. Only in a fight, at a living enemy that the
    /// action will reach, standing still when it has a cast, and off its recast. An enemy-aimed action's range is
    /// left to the scheduler, which measures it the way the game does.
    /// </summary>
    public static string? Reject(in TransformAction action, in TransformFacts facts)
    {
        if (!facts.InCombat)
            return "not in combat";
        if (!facts.EnemyAlive)
            return "no enemy";
        if (action.Aim != TransformAim.Enemy && facts.DistanceToEnemy > ReachOf(action, facts.EnemyHitbox))
            return "enemy out of reach";
        if (action.CastTime > 0f && facts.IsMoving)
            return "needs a hard cast (moving)";
        if (!facts.Ready)
            return "on recast";
        return null;
    }

    // a blast around the character reaches an enemy's edge, a placed circle or a dash is measured to its centre
    private static float ReachOf(in TransformAction action, float enemyHitbox)
        => action.Aim == TransformAim.Self ? action.Reach + enemyHitbox : action.Reach;
}
