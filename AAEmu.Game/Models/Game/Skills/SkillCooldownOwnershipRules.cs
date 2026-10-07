using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Whose cooldown table a cast arms and checks.
/// </summary>
/// <remarks>
/// A skill fired from a mount's or pet's bar is cast with the mount as the caster, because the hull/unit
/// requirement rows are the ones that pass against the mount. The cooldown, though, belongs to the player:
/// retail keeps it keyed by the skill, so it survives despawning the mount, and a second mount carrying the
/// same skill shows the same timer — one <c>mount_skills.skill_id</c> is granted to as many as 56 mounts
/// (17092 is one). Only simulation-driven casts — NPC AI, pet auto-attack, plot tasks, which pass
/// <c>bypassGcd</c> — keep their pacing on the unit itself.
/// </remarks>
public static class SkillCooldownOwnershipRules
{
    /// <summary>
    /// The unit whose <see cref="Unit.Cooldowns"/> the cast uses. <paramref name="resolveUnit"/> is the world
    /// lookup for an owner ObjId; when it resolves nothing the caster keeps its own table, so an unowned or
    /// half-torn-down summon can never lose its pacing.
    /// </summary>
    public static Unit CooldownOwner(Unit caster, bool playerInitiated, Func<uint, Unit> resolveUnit)
    {
        if (caster == null)
            return null;

        if (!playerInitiated)
            return caster;

        switch (caster)
        {
            case Units.Mate mate when mate.OwnerObjId != 0:
                return resolveUnit?.Invoke(mate.OwnerObjId) ?? caster;
            case Units.Slave slave when slave.Summoner != null:
                return slave.Summoner;
            default:
                return caster;
        }
    }
}
