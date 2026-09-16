using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Templates;

namespace AAEmu.Game.Models.Game.Skills;

/// <summary>
/// What caused a hit, which is what the four sub-kinds of the <c>remove_on_*</c> grid distinguish.
/// </summary>
/// <remarks>
/// <para>
/// Every family in the grid (<c>attack_*</c>, <c>attacked_*</c>, <c>damage_*</c>, <c>damaged_*</c>) has the
/// same four members: <c>_etc</c>, <c>_spell_dot</c>, <c>_etc_dot</c> and <c>_buff_trigger</c>. The
/// shipped rows say the sub-kind is <em>additive</em>: all 31 carriers of
/// <c>remove_on_attack_spell_dot</c>, all 31 of <c>remove_on_attack_etc_dot</c> and 338 of the 344
/// carriers of <c>remove_on_attack_buff_trigger</c> carry <c>remove_on_attack_etc</c> as well (the same
/// holds on the other three sides: 86/86 attacked, 301/301 damage, 680/697 damaged), so
/// <c>_etc</c> is the umbrella "any hit of this side" and the other three are the narrower causes
/// raised in addition to it. Ten rows in the whole table are the other way round — they carry a narrow
/// flag with no umbrella (6 <c>attack_buff_trigger</c>: 선장의 통찰력 4690/15246/26079 and
/// 불/얼음/바람의 통찰력 17371-17373, 1 <c>damaged_spell_dot</c>: 순교자의 복수 4159, 3
/// <c>damaged_buff_trigger</c>: 22136/22243/23432) — which is why the narrow members have to be raised at
/// all, and 8 umbrella carriers name no narrow cause, i.e. any hit of that side ends them.
/// </para>
/// <para>
/// What separates the members is the cause of the hit, not its damage type alone: an ordinary skill or
/// weapon hit, a damage-over-time tick (split into magic and everything else, which is the split
/// <c>DamageEffect</c> already uses), or a hit produced by a buff trigger. The <c>*_BuffTrigger</c>
/// columns are not about a buff's own trigger rows — 0 of the 344 <c>remove_on_attack_buff_trigger</c>
/// carriers and 0 of the 301 <c>remove_on_damage_buff_trigger</c> carriers have a matching
/// <c>buff_triggers</c> row of that event kind, and only 58 buffs in the table have an event_id 1 row at
/// all — they are about hits whose source is a trigger.
/// </para>
/// </remarks>
public enum BuffHitCause
{
    /// <summary>An ordinary skill, weapon or effect hit.</summary>
    Ordinary,
    /// <summary>A tick of a magic damage-over-time buff.</summary>
    SpellDot,
    /// <summary>A tick of a damage-over-time buff of any other damage type.</summary>
    EtcDot,
    /// <summary>A hit applied by a buff trigger.</summary>
    BuffTrigger
}

/// <summary>
/// The pure half of the <c>buffs.remove_on_*</c> grid, the three columns beside it, and
/// <c>buff_breakers</c>: which flag an event raises, whether a live instance carries it, and whether a
/// landed buff breaks a buff it names.
/// </summary>
/// <remarks>
/// One flag is deliberately left without a raiser: <c>remove_on_exempt</c> — 7 755 buffs, the largest of
/// the flags that had none — waits for a unit to become exempt, and this build has no such state to come
/// from. <c>Character</c> carries <c>CrimePoint</c>, <c>CrimeRecord</c> and <c>JuryPoint</c> and nothing
/// sets or clears an exemption; the only other <c>Exempt</c> in the tree is the unrelated
/// <c>buffs.exempt</c> column (141 rows) and <see cref="BuffImmunityRules"/>'s <c>immune_except_*</c>
/// grant exception. The mapping is kept here so a raise can be added where the state is.
/// </remarks>
public static class BuffRemoveOnRules
{
    /// <summary>
    /// The cause of a hit, from what the damage path knows about its source.
    /// </summary>
    /// <param name="isTrigger">The effect was applied by a buff trigger rather than a cast.</param>
    /// <param name="isDotTick">The effect came from the tick effects of the buff it belongs to.</param>
    /// <param name="isMagic">The hit's damage type is magic, i.e. a spell DoT rather than an etc DoT.</param>
    public static BuffHitCause HitCause(bool isTrigger, bool isDotTick, bool isMagic)
    {
        if (isTrigger)
            return BuffHitCause.BuffTrigger;
        if (!isDotTick)
            return BuffHitCause.Ordinary;
        return isMagic ? BuffHitCause.SpellDot : BuffHitCause.EtcDot;
    }

    /// <summary>
    /// Flags the attacker's own buffs get for a hit it made, umbrella first.
    /// </summary>
    public static IReadOnlyList<BuffRemoveOn> AttackFlags(BuffHitCause cause) => cause switch
    {
        BuffHitCause.SpellDot => [BuffRemoveOn.AttackEtc, BuffRemoveOn.AttackSpellDot],
        BuffHitCause.EtcDot => [BuffRemoveOn.AttackEtc, BuffRemoveOn.AttackEtcDot],
        BuffHitCause.BuffTrigger => [BuffRemoveOn.AttackEtc, BuffRemoveOn.AttackBuffTrigger],
        _ => [BuffRemoveOn.AttackEtc]
    };

    /// <summary>
    /// Flags the target's own buffs get for a hit it took, umbrella first.
    /// </summary>
    public static IReadOnlyList<BuffRemoveOn> AttackedFlags(BuffHitCause cause) => cause switch
    {
        BuffHitCause.SpellDot => [BuffRemoveOn.AttackedEtc, BuffRemoveOn.AttackedSpellDot],
        BuffHitCause.EtcDot => [BuffRemoveOn.AttackedEtc, BuffRemoveOn.AttackedEtcDot],
        BuffHitCause.BuffTrigger => [BuffRemoveOn.AttackedEtc, BuffRemoveOn.AttackedBuffTrigger],
        _ => [BuffRemoveOn.AttackedEtc]
    };

    /// <summary>
    /// Flags the attacker's own buffs get once its hit actually took health off the target.
    /// </summary>
    public static IReadOnlyList<BuffRemoveOn> DamageFlags(BuffHitCause cause) => cause switch
    {
        BuffHitCause.SpellDot => [BuffRemoveOn.DamageEtc, BuffRemoveOn.DamageSpellDot],
        BuffHitCause.EtcDot => [BuffRemoveOn.DamageEtc, BuffRemoveOn.DamageEtcDot],
        BuffHitCause.BuffTrigger => [BuffRemoveOn.DamageEtc, BuffRemoveOn.DamageBuffTrigger],
        _ => [BuffRemoveOn.DamageEtc]
    };

    /// <summary>
    /// Flags the target's own buffs get once a hit actually took health off it.
    /// </summary>
    public static IReadOnlyList<BuffRemoveOn> DamagedFlags(BuffHitCause cause) => cause switch
    {
        BuffHitCause.SpellDot => [BuffRemoveOn.DamagedEtc, BuffRemoveOn.DamagedSpellDot],
        BuffHitCause.EtcDot => [BuffRemoveOn.DamagedEtc, BuffRemoveOn.DamagedEtcDot],
        BuffHitCause.BuffTrigger => [BuffRemoveOn.DamagedEtc, BuffRemoveOn.DamagedBuffTrigger],
        _ => [BuffRemoveOn.DamagedEtc]
    };

    /// <summary>
    /// Whether a skill is one of the weapon auto-attacks, which is what <c>remove_on_autoattack</c>
    /// (146 buffs) waits for.
    /// </summary>
    /// <remarks>
    /// The three basic attacks the client casts are the skills themselves: <b>2 근접 공격, 3 Offhand and
    /// 4 원거리 공격</b>. That is how the rest of the server tests for an auto-attack — <c>Skill.cs</c>'s
    /// anti-spam pacing and <c>CSStartSkillPacket</c>'s <c>StopAutoAttack</c> branch both test ids 2, 3
    /// or 4 — so the ids are used here too rather than inferred from a column.
    ///
    /// This is deliberately NOT read from <c>weapon_slot_for_autoattack_id</c>. That column looks like the
    /// content's own marker (it is 15/16/17 on those three rows) but it is not exclusive to them: the
    /// shipped distribution is -1 on 4,337 rows, 0 on 33,163 and above zero on <b>543</b> (490 at slot 15,
    /// one at 16, 47 at 17 and five at 18). Reading it as "is an auto-attack" raised the flag for 10399
    /// 방패 휘두르기, 12619 올려치기, 16287 질주, 16064 활쏘기, the mount attacks and a long tail of boss
    /// abilities — and 41 of the 146 carrier buffs set no other skill or attack removal flag, so those
    /// would have dropped on a boss ability rather than on a basic attack.
    ///
    /// The carriers are the poses an attack interrupts: the bard songs 656-667, 연주/율동 performance
    /// buffs, 6176 관악기 연주, 은신 896/6942/22095, 질주 5516/31557 and the ship's 11487-11503 어군 탐색.
    /// </remarks>
    public static bool IsAutoAttack(uint skillId) => skillId is 2 or 3 or 4;

    /// <summary>
    /// Whether a live instance of <paramref name="template"/> is ended by <paramref name="on"/>.
    /// </summary>
    /// <remarks>
    /// This is the table <c>Buffs.TriggerRemoveOn</c> used to spell out as a 28-branch if-chain, with the
    /// same meaning per flag. Two flags carry data in <paramref name="value"/>: <c>StartSkill</c> passes
    /// the tag of the skill being started (the buff is ended unless it carries that tag, i.e. it was
    /// exempted), and <c>SourceDead</c> passes the object id of the dead unit, which has to be the
    /// instance's own caster — that is the one place a removal is scoped to a single caster's instance,
    /// which is what keeps one caster's death from ending another caster's copy of the same family.
    /// </remarks>
    /// <param name="templateCarriesTag">
    /// Whether the instance's buff carries a tag, used by <c>StartSkill</c> only.
    /// </param>
    public static bool Matches(BuffRemoveOn on, BuffTemplate template, uint value, uint instanceCasterObjId,
        Func<uint, bool> templateCarriesTag = null)
    {
        if (template == null)
            return false;

        return on switch
        {
            BuffRemoveOn.SourceDead => template.RemoveOnSourceDead && value == instanceCasterObjId,
            BuffRemoveOn.UseSkill => template.RemoveOnUseSkill,
            BuffRemoveOn.Move => template.RemoveOnMove,
            BuffRemoveOn.Death => template.RemoveOnDeath,
            BuffRemoveOn.Exempt => template.RemoveOnExempt,
            BuffRemoveOn.Land => template.RemoveOnLand,
            BuffRemoveOn.Interaction => template.RemoveOnInteraction,
            BuffRemoveOn.Unmount => template.RemoveOnUnmount &&
                                    UnmountMatches(template.RemoveOnUnmountAttachPointId, value),
            BuffRemoveOn.Mount => template.RemoveOnMount,
            BuffRemoveOn.Unbond => template.RemoveOnUnbond,
            BuffRemoveOn.StartSkill => template.RemoveOnStartSkill &&
                                       (value == 0 || templateCarriesTag?.Invoke(value) != true),
            BuffRemoveOn.AttackSpellDot => template.RemoveOnAttackSpellDot,
            BuffRemoveOn.AttackEtcDot => template.RemoveOnAttackEtcDot,
            BuffRemoveOn.AttackBuffTrigger => template.RemoveOnAttackBuffTrigger,
            BuffRemoveOn.AttackEtc => template.RemoveOnAttackEtc,
            BuffRemoveOn.AttackedSpellDot => template.RemoveOnAttackedSpellDot,
            BuffRemoveOn.AttackedEtcDot => template.RemoveOnAttackedEtcDot,
            BuffRemoveOn.AttackedBuffTrigger => template.RemoveOnAttackedBuffTrigger,
            BuffRemoveOn.AttackedEtc => template.RemoveOnAttackedEtc,
            BuffRemoveOn.DamageSpellDot => template.RemoveOnDamageSpellDot,
            BuffRemoveOn.DamageEtcDot => template.RemoveOnDamageEtcDot,
            BuffRemoveOn.DamageBuffTrigger => template.RemoveOnDamageBuffTrigger,
            BuffRemoveOn.DamageEtc => template.RemoveOnDamageEtc,
            BuffRemoveOn.DamagedSpellDot => template.RemoveOnDamagedSpellDot,
            BuffRemoveOn.DamagedEtcDot => template.RemoveOnDamagedEtcDot,
            BuffRemoveOn.DamagedBuffTrigger => template.RemoveOnDamagedBuffTrigger,
            BuffRemoveOn.DamagedEtc => template.RemoveOnDamagedEtc,
            BuffRemoveOn.AutoAttack => template.RemoveOnAutoAttack,
            BuffRemoveOn.Summoned => template.RemoveBySummoned,
            BuffRemoveOn.ChangeEquipments => IsEquipmentChangeMasked(template.RemoveOnChangeEquipments, value),
            _ => false
        };
    }

    /// <summary>
    /// Whether an unmount from <paramref name="unmountedAttachPoint"/> ends a buff that unmounting
    /// removes.
    /// </summary>
    /// <remarks>
    /// <c>remove_on_unmount_attach_point_id</c> names one <c>enum_attach_point</c> id and narrows
    /// <c>remove_on_unmount</c> to that seat: 719 of the 771 buffs carrying a point name 1 Driver
    /// (the bard songs, 떠오르기 181, 마상 수비 389), 23 name 80 Telescope, and the rest name passengers.
    /// A buff with no point keeps the pre-existing meaning — any unmount removes it (130 of the 780
    /// <c>remove_on_unmount</c> carriers), and so does an unmount whose seat the server could not resolve
    /// (<paramref name="unmountedAttachPoint"/> 0), because the seat being unknown is not evidence that
    /// the buff should stay.
    /// </remarks>
    public static bool UnmountMatches(int buffAttachPointId, uint unmountedAttachPoint) =>
        buffAttachPointId == 0 || unmountedAttachPoint == 0 || buffAttachPointId == unmountedAttachPoint;

    /// <summary>
    /// Whether a change of the equipment in <paramref name="slot"/> ends a buff carrying
    /// <paramref name="mask"/>, which is <c>remove_on_change_equipments</c> read as a bitmask over
    /// <c>enum_equip_slot</c> ids.
    /// </summary>
    /// <remarks>
    /// The four shipped rows decode exactly that way: 15733/29615 감정 표현_궁수 carry 131072 = bit 17
    /// Ranged (the archer pose ends when the bow changes), 31644 테스트 carries 32768 = bit 15 Mainhand,
    /// and 29238 잠재능력 발현 carries 134733823, which is every armour, weapon and cosplay slot
    /// (0-12, 14-18, 27) and nothing else. The column is <c>integer(8)</c>, i.e. signed 64-bit, so the
    /// slot has to stay inside 0-63.
    /// </remarks>
    public static bool IsEquipmentChangeMasked(long mask, uint slot) =>
        mask != 0 && slot < 64 && (mask & (1L << (int)slot)) != 0;

    /// <summary>
    /// Whether a buff carrying <paramref name="breakerTags"/> removes the buff <paramref name="victimBuffId"/>
    /// through <c>buff_breakers</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>buff_breakers(id, buff_id, buff_tag_id)</c> — 2 478 rows over 823 distinct buff ids and 211
    /// distinct tags — reads as "a buff carrying <c>buff_tag_id</c> that lands removes the buff
    /// <c>buff_id</c>", i.e. the tag identifies the arriving buff and the id names the victim, not the
    /// other way round. The data is unambiguous on that: tag 4526 망명자 지우는 버프 is carried by exactly
    /// one buff, 25816 망명자 버프를 지우는 버프 ("the buff that erases the exile buff"), and its only row
    /// removes the six 망명자 exile buffs; tag 5495 춤 입력해야 하는 상태 is carried by 30037-30184
    /// "잭슨의 면 장갑 기술 N개 랜덤 부여" (grant N random glove techniques) and its rows remove the
    /// individual 30034-30192 technique buffs; tag 4699 석상체크 해제 is carried by 26345
    /// 석상 체크 완료 해제 ("statue check complete release") and its rows remove 26341-26344
    /// 체크 완료. Reading it the other way would have the exile buff erase the eraser.
    /// </para>
    /// <para>
    /// The 227-row tags are the crowd control ones — 3108 구속, 97 수면, 6 기절, 107 넘어짐,
    /// 13 창 꽂힘, 12 공포, 99 침묵, 27 발묶임 — and their victims are the channeled states a CC
    /// interrupts (the bard songs, 연주/율동 performance, the boat sail animations), which is the
    /// CC-break rule the client draws.
    /// </para>
    /// </remarks>
    /// <param name="breakerTags">Tags of the buff that just landed.</param>
    /// <param name="victimBuffId">The buff id a row would remove.</param>
    /// <param name="brokenBuffIdsOfTag">The <c>buff_id</c>s listed under a tag, i.e. <c>buff_breakers</c> read by tag.</param>
    public static bool BreaksBuff(IReadOnlyCollection<uint> breakerTags, uint victimBuffId,
        Func<uint, IReadOnlyList<uint>> brokenBuffIdsOfTag)
    {
        if (breakerTags == null || breakerTags.Count == 0 || brokenBuffIdsOfTag == null)
            return false;

        foreach (var tag in breakerTags)
        {
            var victims = brokenBuffIdsOfTag(tag);
            if (victims == null)
                continue;

            for (var i = 0; i < victims.Count; i++)
                if (victims[i] == victimBuffId)
                    return true;
        }

        return false;
    }
}
