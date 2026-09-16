using AAEmu.Game.Models.Game.Skills.Static;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// The <c>skills.use_condition_bits</c> gate: the states the source has to be in for a cast to be
/// allowed, plus the learned-skill rule and the target alive/dead rule for direct casts.
/// </summary>
/// <remarks>
/// Bit <c>n</c> is <c>enum_skill_use_condition_kinds</c> id <c>n</c>; 7,481 shipped skills differ from
/// the default of 1.
///
/// Two groups of bits behave differently, and the data is what separates them:
/// <list type="bullet">
/// <item><b>Requirements.</b> <c>source_alive</c> (1) is set on 37,568 skills — being alive is the
/// normal case. <c>source_dead</c> (2) is set on 742 and nothing else gates them: 허수아비가 부서짐, 분신
/// 사망, NPC 디스폰 시 연출 are death-triggered skills and carry 2 without 1. So the pair selects a
/// state rather than adding one.</item>
/// <item><b>Permissions.</b> 강인한 의지, 활력 방패 and 기선 제압 carry 20513 = bits 1, 6, 13, 15, i.e.
/// stun <i>and</i> sleep <i>and</i> silence together. A mask cannot require three mutually exclusive
/// states at once, so those bits mean "this skill may be used while in that state". A skill without the
/// bit is blocked by the state — which is what the retail client shows by greying the icon out.</item>
/// </list>
/// The 60 skills carrying 28735 (bits 1-7 plus 13-15) confirm the second reading: they claim to be
/// usable alive or dead, mounted or not, and under every disabling effect, which only makes sense as a
/// permission set.
///
/// <b>Not enforced, with reasons.</b>
/// <list type="bullet">
/// <item><c>source_mount</c> (3) / <c>source_mount_mate</c> (4) / <c>source_mount_slave</c> (5). Reading
/// them as requirements matches most of the data — 3,330 skills carry 1|3 and 1,690 of the 2,080 skills
/// named by <c>mount_skills</c> carry 3 — but 28735 sets 2|3|4|5|6|7 at once, which cannot be a
/// requirement. Without a client to confirm which is which, blocking a cast because the caster is not
/// mounted would reject ordinary casts made from a mount.</item>
/// <item><c>source_cannot_use_while_walk</c> (8) and <c>source_cannot_use_while_jump</c> (16). No
/// server-side walking or jumping state exists to test; the client gates both, and movement enforcement
/// is E9's.</item>
/// <item><c>allow_to_prisoner</c> (7), <c>source_no_slave</c> (11), <c>source_not_collided</c> (12).
/// No prisoner, slave-attachment or collision state is modelled at cast time.</item>
/// </list>
/// <c>source_not_swim</c> (9) and <c>source_should_swim</c> (10) are enforced through
/// <see cref="CasterState.IsSwimming"/>, and <c>source_crippled</c> (14) is deliberately left out of
/// the disabling set: crippled is a root, and a rooted character still casts.
/// </remarks>
public static class SkillUseConditionRules
{
    // enum_skill_use_condition_kinds
    public const int SourceAlive = 1;
    public const int SourceDead = 2;
    public const int SourceMount = 3;
    public const int SourceMountMate = 4;
    public const int SourceMountSlave = 5;
    public const int SourceStun = 6;
    public const int AllowToPrisoner = 7;
    public const int SourceCannotUseWhileWalk = 8;
    public const int SourceNotSwim = 9;
    public const int SourceShouldSwim = 10;
    public const int SourceNoSlave = 11;
    public const int SourceNotCollided = 12;
    public const int SourceSleep = 13;
    public const int SourceCrippled = 14;
    public const int SourceSilence = 15;
    public const int SourceCannotUseWhileJump = 16;

    /// <summary>The states a cast is evaluated against, read once per cast from the caster.</summary>
    public readonly record struct CasterState(
        bool IsDead,
        bool IsStunned,
        bool IsAsleep,
        bool IsSilenced,
        bool IsSwimming);

    /// <summary>
    /// The failure this skill's condition bits produce for this caster, or <see langword="null"/> when
    /// the cast may proceed.
    /// </summary>
    public static SkillResult? Evaluate(long useConditionBits, in CasterState state)
    {
        // Alive/dead first: a corpse has to say so through source_dead, and a death-triggered skill
        // has to say so through the same bit rather than by carrying source_alive.
        if (state.IsDead)
        {
            if (!HasBit(useConditionBits, SourceDead))
                return SkillResult.SourceDied;
        }
        else if (!HasBit(useConditionBits, SourceAlive))
        {
            return SkillResult.SourceAlive;
        }

        // Permissions: the state blocks the cast unless the skill states it is usable in it.
        // Sleep reuses the stun result: the client's SkillResult table has no sleep-specific entry and
        // the two states bar a cast identically.
        if (state.IsStunned && !HasBit(useConditionBits, SourceStun))
            return SkillResult.CannotCastInStun;
        if (state.IsAsleep && !HasBit(useConditionBits, SourceSleep))
            return SkillResult.CannotCastInStun;
        if (state.IsSilenced && !HasBit(useConditionBits, SourceSilence))
            return SkillResult.Silence;

        if (state.IsSwimming && HasBit(useConditionBits, SourceNotSwim))
            return SkillResult.CannotCastInSwimming;
        if (!state.IsSwimming && HasBit(useConditionBits, SourceShouldSwim))
            return SkillResult.OnlyDuringSwimming;

        return null;
    }

    /// <summary>Reads the state the bits are evaluated against off a live unit.</summary>
    public static CasterState ReadState(Unit unit)
    {
        if (unit == null)
            return default;

        return new CasterState(
            unit.IsDead,
            unit.Buffs.HasEffectsMatchingCondition(e => e.Template.Stun),
            unit.Buffs.HasEffectsMatchingCondition(e => e.Template.Sleep),
            unit.Buffs.HasEffectsMatchingCondition(e => e.Template.Silence),
            unit.IsUnderWater);
    }

    /// <summary>
    /// Whether a character may cast a skill it holds no record of. Ability skills (<c>ability_id</c>
    /// other than 0) are the ones that have to be learned; the basic attacks, the racial defaults, the
    /// common skills every character has and item-granted casts are not.
    /// </summary>
    /// <remarks>
    /// A live buff can grant a skill (<c>buff_skills</c>, B6), and
    /// <c>CharacterSkills.HasSkill</c> already reports those as held, so a granted skill passes here
    /// exactly like a learned one.
    /// </remarks>
    public static bool AllowsUnlearnedCast(AbilityType abilityId, bool isItemCast, bool isDefaultSkill, bool isCommonSkill)
        => abilityId == AbilityType.General || isItemCast || isDefaultSkill || isCommonSkill;

    /// <summary>
    /// The failure shown when a character casts an ability skill it has no record of.
    /// <c>skill_urk_trained_skill</c> is the client string for it.
    /// </summary>
    public const SkillResult UnlearnedSkillResult = SkillResult.UrkTrainedSkill;

    /// <summary>
    /// Target alive/dead filtering, using the same reading as the plot target filter
    /// (<c>PlotTargetInfo.FilterTargets</c>): <c>target_alive='f'</c> means only dead units qualify and
    /// <c>target_dead='f'</c> means dead units do not.
    /// </summary>
    public static bool AllowsTarget(bool targetAlive, bool targetDead, bool targetIsDead)
    {
        if (!targetAlive && targetIsDead)
            return true;
        if (!targetAlive)
            return false;
        if (!targetDead && targetIsDead)
            return false;
        return true;
    }

    private static bool HasBit(long bits, int kind) => (bits & (1L << (kind - 1))) != 0;
}
