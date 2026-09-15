using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Skills;

/// <summary>
/// The two tag tables the 10.0.2.13 content database ships and that nothing loaded until now:
/// <c>tagged_immune_buffs</c> (2 645 rows: while buff X is up, refuse tag Y) and
/// <c>tagged_require_buffs</c> (309 rows: buff X may only apply while the target carries tag Y).
/// </summary>
/// <remarks>
/// The ids and row shapes are the live ones, read through the <c>tags</c> table: 93 동결 refuses tag
/// 919 차가운 발걸음; 5936 속이 거북한 거북 refuses tag 216 무적 면역 and carries
/// <c>immune_except_creator='t'</c> with <c>immune_except_skill_tag_id</c> 1053 놀이기구 사용; 20248
/// carries <c>immune_except_creator_relation_check='t'</c> with relation 8 (family); 4627 가벼운 발걸음
/// requires tag 831 무겁다.
/// </remarks>
public class BuffImmunityRulesTests
{
    private const uint FreezeBuffId = 93; // 동결
    private const uint FreezeTagId = 919; // 차가운 발걸음
    private const uint RideBuffId = 5936; // 속이 거북한 거북
    private const uint InvincibleImmuneTagId = 216; // 무적 면역
    private const uint RideSkillTagId = 1053; // 놀이기구 사용
    private const uint FamilyRelationId = 8; // enum_skill_target_relation: family
    private const uint HostileRelationId = 4; // enum_skill_target_relation: hostile
    private const uint HeavyTagId = 831; // 무겁다
    private const uint LightStepBuffId = 4627; // 가벼운 발걸음
    private const uint CasterObjId = 20u;
    private const uint CreatorObjId = 21u;

    #region tag immunity

    [Test]
    public async Task TagTheOwnerIsImmuneTo_IsRefused()
    {
        var refused = Refused(
            [FreezeTagId],
            [ActiveBuff(FreezeBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [FreezeBuffId] = [FreezeTagId] });

        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task TagTheOwnerIsNotImmuneTo_Passes()
    {
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(FreezeBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [FreezeBuffId] = [FreezeTagId] });

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task OneOfSeveralCandidateTagsIsRefused_IsRefused()
    {
        var refused = Refused(
            [InvincibleImmuneTagId, FreezeTagId],
            [ActiveBuff(FreezeBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [FreezeBuffId] = [FreezeTagId] });

        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task CandidateWithoutTags_Passes()
    {
        var refused = Refused(
            [],
            [ActiveBuff(FreezeBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [FreezeBuffId] = [FreezeTagId] });

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task NoImmunityRowsAtAll_PassesEverything()
    {
        // The neutral case: with nothing in tagged_immune_buffs every candidate applies, which is what
        // the server did for every candidate before this table was loaded.
        var refused = Refused(
            [FreezeTagId, InvincibleImmuneTagId],
            [ActiveBuff(FreezeBuffId, CreatorObjId), ActiveBuff(RideBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>>());

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task NoActiveBuffs_PassesEverything()
    {
        var refused = Refused(
            [FreezeTagId],
            [],
            new Dictionary<uint, List<uint>> { [FreezeBuffId] = [FreezeTagId] });

        await Assert.That(refused).IsFalse();
    }

    #endregion

    #region immune_except_creator

    [Test]
    public async Task CreatorException_TheCreatorApplyingTheCandidate_Passes()
    {
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, creatorObjId: CasterObjId, immuneExceptCreator: true)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] });

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task CreatorException_AnotherUnitApplyingTheCandidate_IsStillRefused()
    {
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, creatorObjId: CreatorObjId, immuneExceptCreator: true)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] });

        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task CreatorException_UnknownCaster_IsStillRefused()
    {
        // A buff applied with no caster modelled cannot equal anybody, so the exception does not fire.
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, creatorObjId: CreatorObjId, immuneExceptCreator: true)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] },
            casterObjId: 0);

        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task CreatorException_WithoutTheFlag_TheCreatorIsRefusedToo()
    {
        var refused = Refused(
            [FreezeTagId],
            [ActiveBuff(FreezeBuffId, creatorObjId: CasterObjId)],
            new Dictionary<uint, List<uint>> { [FreezeBuffId] = [FreezeTagId] });

        await Assert.That(refused).IsTrue();
    }

    #endregion

    #region immune_except_skill_tag_id

    [Test]
    public async Task SkillTagException_TheCastingSkillCarriesTheTag_Passes()
    {
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, CreatorObjId, immuneExceptSkillTagId: RideSkillTagId)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] },
            casterSkillTags: [RideSkillTagId]);

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task SkillTagException_AnotherSkillTag_IsStillRefused()
    {
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, CreatorObjId, immuneExceptSkillTagId: RideSkillTagId)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] },
            casterSkillTags: [HeavyTagId]);

        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task SkillTagException_NoCastingSkill_IsStillRefused()
    {
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, CreatorObjId, immuneExceptSkillTagId: RideSkillTagId)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] });

        await Assert.That(refused).IsTrue();
    }

    #endregion

    #region immune_except_creator_relation_check

    [Test]
    public async Task RelationException_TheCasterRelationMatches_Passes()
    {
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, CreatorObjId, relationCheck: true, relationId: FamilyRelationId)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] },
            relationMatches: relationId => relationId == FamilyRelationId);

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task RelationException_TheCasterRelationDoesNotMatch_IsStillRefused()
    {
        var consulted = 0u;
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, CreatorObjId, relationCheck: true, relationId: HostileRelationId)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] },
            relationMatches: relationId =>
            {
                consulted = relationId;
                return relationId == FamilyRelationId;
            });

        await Assert.That(refused).IsTrue();
        await Assert.That(consulted).IsEqualTo(HostileRelationId);
    }

    [Test]
    public async Task RelationException_WithoutTheCheckTheRelationIsNotConsulted()
    {
        var consulted = false;
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, CreatorObjId, relationId: FamilyRelationId)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] },
            relationMatches: _ =>
            {
                consulted = true;
                return true;
            });

        await Assert.That(refused).IsTrue();
        await Assert.That(consulted).IsFalse();
    }

    #endregion

    #region several grants

    [Test]
    public async Task TwoGrants_OneExemptOneNot_IsRefused()
    {
        // Each grant is judged on its own exceptions; an exemption under one does not clear the other.
        var refused = Refused(
            [InvincibleImmuneTagId],
            [
                ActiveBuff(RideBuffId, creatorObjId: CasterObjId, immuneExceptCreator: true),
                ActiveBuff(FreezeBuffId, CreatorObjId)
            ],
            new Dictionary<uint, List<uint>>
            {
                [RideBuffId] = [InvincibleImmuneTagId],
                [FreezeBuffId] = [InvincibleImmuneTagId]
            });

        await Assert.That(refused).IsTrue();
    }

    #endregion

    #region tagged_require_buffs

    [Test]
    public async Task RequiredTag_TheTargetDoesNotCarryIt_IsReported()
    {
        var missing = BuffImmunityRules.FirstMissingRequiredTag([HeavyTagId], _ => false);

        await Assert.That(missing).IsEqualTo(HeavyTagId);
    }

    [Test]
    public async Task RequiredTag_TheTargetCarriesIt_IsZero()
    {
        var missing = BuffImmunityRules.FirstMissingRequiredTag([HeavyTagId], tagId => tagId == HeavyTagId);

        await Assert.That(missing).IsEqualTo(0u);
    }

    [Test]
    public async Task RequiredTag_SeveralRows_ReportsTheFirstOneMissing()
    {
        var missing = BuffImmunityRules.FirstMissingRequiredTag(
            [HeavyTagId, InvincibleImmuneTagId],
            tagId => tagId == HeavyTagId);

        await Assert.That(missing).IsEqualTo(InvincibleImmuneTagId);
    }

    [Test]
    public async Task RequiredTag_NoRows_IsZero()
    {
        var missing = BuffImmunityRules.FirstMissingRequiredTag([], _ => false);

        await Assert.That(missing).IsEqualTo(0u);
    }

    #endregion

    private static bool Refused(
        uint[] candidateTags,
        Buff[] activeBuffs,
        Dictionary<uint, List<uint>> immunityTable,
        uint casterObjId = CasterObjId,
        uint[] casterSkillTags = null,
        Func<uint, bool> relationMatches = null)
    {
        return BuffImmunityRules.IsRefusedByTagImmunity(
            candidateTags,
            activeBuffs,
            buffId => immunityTable.TryGetValue(buffId, out var tags) ? tags : [],
            casterObjId,
            casterSkillTags ?? [],
            relationMatches ?? (_ => false));
    }

    /// <summary>
    /// An active buff as <c>Buffs</c> holds it: the template carries the <c>immune_except_*</c> columns
    /// and <see cref="Buff.Caster"/> is the unit that created it.
    /// </summary>
    private static Buff ActiveBuff(
        uint buffId,
        uint creatorObjId,
        bool immuneExceptCreator = false,
        uint immuneExceptSkillTagId = 0,
        bool relationCheck = false,
        uint relationId = 0)
    {
        var template = new BuffTemplate
        {
            Id = buffId,
            ImmuneExceptCreator = immuneExceptCreator,
            ImmuneExceptSkillTagId = immuneExceptSkillTagId,
            ImmuneExceptCreatorRelationCheck = relationCheck,
            ImmuneExceptCreatorRelationId = relationId
        };
        var creator = new Unit { ObjId = creatorObjId };

        return new Buff(new BaseUnit { ObjId = 1 }, creator, new SkillCasterUnit(creatorObjId), template, null,
            DateTime.UtcNow)
        {
            Passive = true
        };
    }
}
