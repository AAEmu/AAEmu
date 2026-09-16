using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Skills.Buffs;
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
/// requires tag 831 무겁다. Of the 283 rows that name a tag their own buff carries, 266 (260 refresh, 3
/// charge_refresh, 3 extend) let the buff's own re-application through and 17 (16 independent, 1 multiple at
/// max_stack 1) refuse it, because for those the refusal is the whole of what the row says.
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
    private const uint PrisonerBuffId = 631; // 수감자, stack_rule_id 5 (extend)
    private const uint PrisonerTagId = 344; // 수감자, carried by 631/2028/3623/4868/8038
    private const uint RageBuffId = 17304; // 살기 최대 누적, stack_rule_id 2 (charge_refresh)
    private const uint RageTagId = 165; // 살기, carried by 870/17304/23505
    private const uint CooldownMarkerBuffId = 25466; // 검은 용의 힘 : 꼬리수집가 쿨타임 체크용, stack_rule_id 6
    private const uint CooldownTagId = 4447; // 검은용 3티어 한손검 쿨타임 체크용, carried by 25466 alone
    private const uint HurryBuffId = 26622; // 빨리 빨리, stack_rule_id 4 (multiple) with max_stack 1
    private const uint HurryImmuneTagId = 4747; // 빨리빨리 면역, carried by 26622 alone
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

    [Test]
    public async Task TheSameBuffIdWhileItIsUp_IsNotRefusedByItsOwnGrant()
    {
        // 93 동결 carries tag 919 and is immune to 919. The row refuses *other* buffs: a second freeze has
        // to refresh (stack_rule_id 1, max_stack 10) instead of being turned away at the immunity gate.
        var refused = Refused(
            [FreezeTagId],
            [ActiveBuff(FreezeBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [FreezeBuffId] = [FreezeTagId] },
            candidateBuffId: FreezeBuffId,
            candidateStackRule: BuffStackRule.Refresh,
            candidateMaxStack: 10);

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task AnExtendSelfGrantingRow_IsNotRefusedByItsOwnGrant()
    {
        // 631 수감자 (stack_rule_id 5, max_stack 1, 1 800 000 ms) is immune to the tag it carries: the
        // re-application has to reach Buffs.AddBuff, where Buff.OverwriteWith adds the new duration to what
        // is left of the old one. Refusing it here is what stopped that extend.
        var refused = Refused(
            [PrisonerTagId],
            [ActiveBuff(PrisonerBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [PrisonerBuffId] = [PrisonerTagId] },
            candidateBuffId: PrisonerBuffId,
            candidateStackRule: BuffStackRule.Extend,
            candidateMaxStack: 1);

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task AChargeRefreshSelfGrantingRow_IsNotRefusedByItsOwnGrant()
    {
        // 17304 살기 최대 누적 (stack_rule_id 2) is immune to tag 165 살기, which it carries itself and shares
        // with 870 and 23505, so its own re-application keeps its charge-refresh path.
        var refused = Refused(
            [RageTagId],
            [ActiveBuff(RageBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [RageBuffId] = [RageTagId] },
            candidateBuffId: RageBuffId,
            candidateStackRule: BuffStackRule.ChargeRefresh,
            candidateMaxStack: 1);

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task AnIndependentSelfGrantingRow_IsRefusedByItsOwnGrant()
    {
        // The 16 independent self-granting rows carry a tag nothing else carries, so refusing the
        // re-application is the whole of what the row says. 25466 and eleven more of them are the
        // 60-second 쿨타임 체크용 markers: letting the re-application through would only replace the live
        // instance and restart the cooldown the marker exists to hold.
        var refused = Refused(
            [CooldownTagId],
            [ActiveBuff(CooldownMarkerBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [CooldownMarkerBuffId] = [CooldownTagId] },
            candidateBuffId: CooldownMarkerBuffId,
            candidateStackRule: BuffStackRule.Independent,
            candidateMaxStack: 1);

        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task AMultipleSelfGrantingRowAtACeilingOfOne_IsRefusedByItsOwnGrant()
    {
        // 26622 빨리 빨리 is stack_rule_id 4 with max_stack 1, so there is no room to grow: the
        // re-application would replace the live instance, and tag 4747 빨리빨리 면역 is carried by 26622 alone.
        var refused = Refused(
            [HurryImmuneTagId],
            [ActiveBuff(HurryBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [HurryBuffId] = [HurryImmuneTagId] },
            candidateBuffId: HurryBuffId,
            candidateStackRule: BuffStackRule.Multiple,
            candidateMaxStack: 1);

        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task AMultipleSelfGrantingRowWithRoom_IsNotRefusedByItsOwnGrant()
    {
        // The same rule above a ceiling of one absorbs the application into the live count, so the buff
        // takes its own stack path instead.
        var refused = Refused(
            [HurryImmuneTagId],
            [ActiveBuff(HurryBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [HurryBuffId] = [HurryImmuneTagId] },
            candidateBuffId: HurryBuffId,
            candidateStackRule: BuffStackRule.Multiple,
            candidateMaxStack: 10);

        await Assert.That(refused).IsFalse();
    }

    [Test]
    public async Task AChargeExtendSelfGrantingRow_IsRefusedByItsOwnGrant()
    {
        // charge_extend (stack_rule_id 3, 39 buffs) has no re-application in Buffs.AddBuff either — it falls
        // to the same replacement branch — so a self-granting row shipping it keeps its grant. No
        // self-granting row ships the rule today, so this is the marker's shape with that rule swapped in.
        var refused = Refused(
            [CooldownTagId],
            [ActiveBuff(CooldownMarkerBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [CooldownMarkerBuffId] = [CooldownTagId] },
            candidateBuffId: CooldownMarkerBuffId,
            candidateStackRule: BuffStackRule.ChargeExtend,
            candidateMaxStack: 1);

        await Assert.That(refused).IsTrue();
    }

    [Test]
    public async Task AnotherBuffCarryingTheSameTag_IsStillRefused()
    {
        // The self-skip is by buff id, not by tag: a different buff carrying 919 is still refused.
        var refused = Refused(
            [FreezeTagId],
            [ActiveBuff(FreezeBuffId, CreatorObjId)],
            new Dictionary<uint, List<uint>> { [FreezeBuffId] = [FreezeTagId] },
            candidateBuffId: 91001);

        await Assert.That(refused).IsTrue();
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

    [Test]
    public async Task RelationException_RelationIdZero_DoesNotExemptEverybody()
    {
        // enum_skill_target_relation 0 is "any", which IsRelationValid answers true for against every
        // caster, so the clause used to exempt everybody and the immunity could never fire. Five buffs
        // ship the check with id 0 (14408 견본 배 무적, 32711 겁먹은 페피 송송, 32712, 32718, 32726) and
        // two of them have immunity rows.
        var consulted = false;
        var refused = Refused(
            [InvincibleImmuneTagId],
            [ActiveBuff(RideBuffId, CreatorObjId, relationCheck: true, relationId: 0)],
            new Dictionary<uint, List<uint>> { [RideBuffId] = [InvincibleImmuneTagId] },
            relationMatches: _ =>
            {
                consulted = true;
                return true; // what IsRelationValid does for Any
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
        Func<uint, bool> relationMatches = null,
        uint candidateBuffId = 0,
        BuffStackRule candidateStackRule = BuffStackRule.Refresh,
        int candidateMaxStack = 1)
    {
        return BuffImmunityRules.IsRefusedByTagImmunity(
            candidateTags,
            new BuffTemplate { Id = candidateBuffId, StackRule = candidateStackRule, MaxStack = candidateMaxStack },
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
