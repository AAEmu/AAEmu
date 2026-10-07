using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// Whose cooldown table a cast arms and checks.
/// </summary>
/// <remarks>
/// A skill fired from a mount's or pet's bar is cast with the mount as the caster, because the hull/unit
/// requirement rows are the ones that pass against the mount. The cooldown, though, belongs to the player:
/// retail keeps it keyed by the skill, so it survives despawning the mount, and a second mount carrying the
/// same skill shows the same timer — one <c>mount_skills.skill_id</c> is granted to as many as 56 mounts.
/// Only simulation-driven casts — NPC AI, pet auto-attack, plot tasks, which pass <c>bypassGcd</c> — keep
/// their pacing on the unit itself. A vehicle is not a mount: its seat holder is not necessarily its
/// summoner, so a vehicle cast keeps the vehicle's own table.
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

        // A mount names its rider, so a mount-bar cast can be attributed to the player. A vehicle cannot:
        // BindSlave lets other players take seats and fire its bar, so the summoner is not necessarily the
        // player who cast — the vehicle keeps its own pacing instead of charging someone who may not have
        // fired at all.
        if (caster is Units.Mate mate && mate.OwnerObjId != 0)
            return resolveUnit?.Invoke(mate.OwnerObjId) ?? caster;

        return caster;
    }
}
