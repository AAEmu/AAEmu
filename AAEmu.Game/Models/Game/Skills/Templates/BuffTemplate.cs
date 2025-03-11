using System;
using System.Collections.Generic;
using System.Linq;

using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Skills.Buffs;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Skills.Utils;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Skills.Templates;

public class BuffTemplate
{
    public uint Id { get; set; }
    public uint BuffId => Id;
    public uint AnimStartId { get; set; }
    public uint AnimEndId { get; set; }
    public int Duration { get; set; }
    public double Tick { get; set; }
    public bool Silence { get; set; }
    public bool Root { get; set; }
    public bool Sleep { get; set; }
    public bool Stun { get; set; }
    public bool Cripled { get; set; }
    public bool Stealth { get; set; }
    public bool RemoveOnSourceDead { get; set; }
    public uint LinkBuffId { get; set; }
    public int TickManaCost { get; set; }
    public BuffStackRule StackRule { get; set; }
    public int InitMinCharge { get; set; }
    public int InitMaxCharge { get; set; }
    public int MaxStack { get; set; }
    public uint DamageAbsorptionTypeId { get; set; }
    public int DamageAbsorptionPerHit { get; set; }
    public int AuraRadius { get; set; }
    public int ManaShieldRatio { get; set; }
    public bool FrameHold { get; set; }
    public bool Ragdoll { get; set; }
    public bool OneTime { get; set; }
    public int ReflectionChance { get; set; }
    public uint ReflectionTypeId { get; set; }
    public uint RequireBuffId { get; set; }
    public bool Taunt { get; set; }
    public bool TauntWithTopAggro { get; set; }
    public bool RemoveOnUseSkill { get; set; }
    public bool MeleeImmune { get; set; }
    public bool SpellImmune { get; set; }
    public bool RangedImmune { get; set; }
    public bool SiegeImmune { get; set; }
    public int ImmuneDamage { get; set; }
    public uint SkillControllerId { get; set; }
    public int ResurrectionHealth { get; set; }
    public int ResurrectionMana { get; set; }
    public bool ResurrectionPercent { get; set; }
    public int LevelDuration { get; set; }
    public int ReflectionRatio { get; set; }
    public int ReflectionTargetRatio { get; set; }
    public bool KnockbackImmune { get; set; }
    public uint ImmuneBuffTagId { get; set; }
    public uint AuraRelationId { get; set; }
    public uint GroupId { get; set; }
    public int GroupRank { get; set; }
    public bool PerUnitCreation { get; set; }
    public float TickAreaRadius { get; set; }
    public uint TickAreaRelationId { get; set; }
    public bool RemoveOnMove { get; set; }
    public bool UseSourceFaction { get; set; }
    public FactionsEnum FactionId { get; set; }
    public bool Exempt { get; set; }
    public int TickAreaFrontAngle { get; set; }
    public int TickAreaAngle { get; set; }
    public bool Psychokinesis { get; set; }
    public bool NoCollide { get; set; }
    public float PsychokinesisSpeed { get; set; }
    public bool RemoveOnDeath { get; set; }
    public uint TickAnimId { get; set; }
    public uint TickActiveWeaponId { get; set; }
    public bool ConditionalTick { get; set; }
    public bool System { get; set; }
    public uint AuraSlaveBuffId { get; set; }
    public bool NonPushable { get; set; }
    public uint ActiveWeaponId { get; set; }
    public int MaxCharge { get; set; }
    public bool DetectStealth { get; set; }
    public bool RemoveOnExempt { get; set; }
    public bool RemoveOnLand { get; set; }
    public bool Gliding { get; set; }
    public int GlidingRotateSpeed { get; set; }
    public bool Knockdown { get; set; }
    public bool TickAreaExcludeSource { get; set; }
    public bool FallDamageImmune { get; set; }
    public BuffKind Kind { get; set; }
    public uint TransformBuffId { get; set; }
    public bool BlankMinded { get; set; }
    public bool Fastened { get; set; }
    public bool SlaveApplicable { get; set; }
    public bool Pacifist { get; set; }
    public bool RemoveOnInteraction { get; set; }
    public bool Crime { get; set; }
    public bool RemoveOnUnmount { get; set; }
    public bool AuraChildOnly { get; set; }
    public bool RemoveOnMount { get; set; }
    public bool RemoveOnStartSkill { get; set; }
    public bool SprintMotion { get; set; }
    public float TelescopeRange { get; set; }
    public uint MainhandToolId { get; set; }
    public uint OffhandToolId { get; set; }
    public uint TickMainhandToolId { get; set; }
    public uint TickOffhandToolId { get; set; }
    public float TickLevelManaCost { get; set; }
    public bool WalkOnly { get; set; }
    public bool CannnotJump { get; set; }
    public uint CrowdBuffId { get; set; }
    public float CrowdRadius { get; set; }
    public int CrowdNumber { get; set; }
    public bool EvadeTelescope { get; set; }
    public float TransferTelescopeRange { get; set; }
    public bool RemoveOnAttackSpellDot { get; set; }
    public bool RemoveOnAttackEtcDot { get; set; }
    public bool RemoveOnAttackBuffTrigger { get; set; }
    public bool RemoveOnAttackEtc { get; set; }
    public bool RemoveOnAttackedSpellDot { get; set; }
    public bool RemoveOnAttackedEtcDot { get; set; }
    public bool RemoveOnAttackedBuffTrigger { get; set; }
    public bool RemoveOnAttackedEtc { get; set; }
    public bool RemoveOnDamageSpellDot { get; set; }
    public bool RemoveOnDamageEtcDot { get; set; }
    public bool RemoveOnDamageBuffTrigger { get; set; }
    public bool RemoveOnDamageEtc { get; set; }
    public bool RemoveOnDamagedSpellDot { get; set; }
    public bool RemoveOnDamagedEtcDot { get; set; }
    public bool RemoveOnDamagedBuffTrigger { get; set; }
    public bool RemoveOnDamagedEtc { get; set; }
    public bool OwnerOnly { get; set; }
    public bool RemoveOnAutoAttack { get; set; }
    public uint SaveRuleId { get; set; }
    public bool AntiStealth { get; set; }
    public float Scale { get; set; }
    public float ScaleDuration { get; set; }
    public bool ImmuneExceptCreator { get; set; }
    public uint ImmuneExceptSkillTagId { get; set; }
    public float FindSchoolOfFishRange { get; set; }
    public uint AnimActionId { get; set; }
    public bool DeadApplicable { get; set; }
    public bool TickAreaUseOriginSource { get; set; }
    public bool RealTime { get; set; }
    public bool DoNotRemoveByOtherSkillController { get; set; }
    public uint CooldownSkillId { get; set; }
    public int CooldownSkillTime { get; set; }
    public bool ManaBurnImmune { get; set; }
    public bool FreezeShip { get; set; }
    public bool CrowdFriendly { get; set; }
    public bool CrowdHostile { get; set; }
    public bool OnActionTime => Tick > 0;

    public List<TickEffect> TickEffects { get; set; }
    public List<BonusTemplate> Bonuses { get; set; }
    public List<DynamicBonusTemplate> DynamicBonuses { get; set; }

    public BuffTemplate()
    {
        TickEffects = new List<TickEffect>();
        Bonuses = new List<BonusTemplate>();
        DynamicBonuses = new List<DynamicBonusTemplate>();
    }

    public void Apply(BaseUnit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
        CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time,
        CompressedGamePackets packetBuilder = null)
    {
        if (RequireBuffId > 0 && !target.Buffs.CheckBuff(RequireBuffId))
            return; //TODO send error?
        if (target.Buffs.CheckBuffImmune(Id))
            return; //TODO  error of immune?
        ushort abLevel = 1;
        if (caster is Character character)
        {
            if (source.Skill != null)
            {
                var template = source.Skill.Template;
                var abilityLevel = character.GetAbLevel((AbilityType)source.Skill.Template.AbilityId);
                if (template.LevelStep != 0)
                    abLevel = (ushort)((abilityLevel / template.LevelStep) * template.LevelStep);
                else
                    abLevel = (ushort)template.AbilityLevel;

                //Dont allow lower than minimum ablevel for skill or infinite debuffs can happen
                abLevel = (ushort)Math.Max(template.AbilityLevel, abLevel);
            }
            else if (source.Buff != null)
            {
                //not sure?
            }
        }
        else
        {
            if (source.Skill != null)
            {
                abLevel = (ushort)source.Skill.Template.AbilityLevel;
            }
        }
        target.Buffs.AddBuff(new Buff(target, caster, casterObj, this, source?.Skill, time) { AbLevel = abLevel });
    }

    public void Start(BaseUnit caster, BaseUnit owner, Buff buff)
    {
        foreach (var template in Bonuses)
        {
            var bonus = new Bonus();
            bonus.Template = template;
            bonus.Value = (int)Math.Round(template.Value + (template.LinearLevelBonus * (buff.AbLevel / 100f)));
            owner.AddBonus(buff.Index, bonus);
        }

        if (buff.Charge == 0)
            buff.Charge = Rand.Next(InitMinCharge, InitMaxCharge);

        if (!buff.Passive)
            owner.BroadcastPacket(new SCBuffCreatedPacket(buff), true);

        // Special properties handling
        if (owner is Character character)
        {
            if (FindSchoolOfFishRange > 0)
                RadarManager.Instance.RegisterForFishSchool(character, FindSchoolOfFishRange);
            if (TransferTelescopeRange > 0)
                RadarManager.Instance.RegisterForPublicTransport(character, TransferTelescopeRange);
            if (TelescopeRange > 0)
                RadarManager.Instance.RegisterForShips(character, TelescopeRange);
            if (character.Buffs.CheckBuff((uint)BuffConstants.Dash))
            {
                var template = new ManaRegenTemplate(character, buff.Template.Tick, buff.Template.TickLevelManaCost, character.Level);
                ManaRegenManager.Instance.Register(character, template);
            }
        }
    }

    public void TimeToTimeApply(BaseUnit caster, BaseUnit owner, Buff buff)
    {
        if (TickAreaRadius > 0)
        {
            DoAreaTick(caster, owner, buff);
            return;
        }
        var mates = MateManager.Instance.GetActiveMates(owner.ObjId);
        foreach (var tickEff in TickEffects)
        {
            if (caster is Character { IsRiding: true })
            {
                if (mates != null)
                {
                    foreach (var mate in mates)
                    {
                        if (tickEff.TargetBuffTagId > 0 &&
                            !mate.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetBuffTagId)))
                            return;
                        if (tickEff.TargetNoBuffTagId > 0 &&
                            mate.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetNoBuffTagId)))
                            return;
                    }
                }
            }
            else
            {
                if (tickEff.TargetBuffTagId > 0 && !owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetBuffTagId)))
                    return;
                if (tickEff.TargetNoBuffTagId > 0 && owner.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetNoBuffTagId)))
                    return;
            }
            var eff = SkillManager.Instance.GetEffectTemplate(tickEff.EffectId);
            if (eff == null)
            {
                return;
            }
            var targetObj = new SkillCastUnitTarget(owner.ObjId);
            var skillObj = new SkillObject(); // TODO ?
            eff.Apply(caster, buff.SkillCaster, owner, targetObj, new CastBuff(buff), new EffectSource(this), skillObj, DateTime.UtcNow);
        }
    }

    public void DoAreaTick(BaseUnit caster, BaseUnit owner, Buff buff)
    {
        var units = WorldManager.GetAround<Unit>(owner, TickAreaRadius);

        owner ??= caster;

        var ownerUnit = owner as Unit;
        if (TickAreaExcludeSource && ownerUnit != null)
            units.Remove(ownerUnit);
        else if (ownerUnit != null && !units.Contains(owner))
            units.Add(ownerUnit);

        units = SkillTargetingUtil.FilterWithRelation((SkillTargetRelation)TickAreaRelationId, (Unit)caster, units).ToList();

        var source = caster;
        //if (TickAreaUseOriginSource)
        //source = (Unit)owner;
        var skillObj = new SkillObject(); // TODO ?

        // Create a copy of the units collection for safe iteration
        var unitsCopy = units.ToList();
        //lock (units)
        {
            foreach (var tickEff in TickEffects)
            {
                var eff = SkillManager.Instance.GetEffectTemplate(tickEff.EffectId);

                foreach (var trg in unitsCopy)
                //foreach (var trg in units)
                {
                    if (tickEff.TargetBuffTagId > 0 &&
                        !trg.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetBuffTagId)))
                        continue;
                    if (tickEff.TargetNoBuffTagId > 0 &&
                        trg.Buffs.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(tickEff.TargetNoBuffTagId)))
                        continue;

                    var targetObj = new SkillCastUnitTarget(trg.ObjId);
                    eff.Apply(source, buff.SkillCaster, trg, targetObj, new CastBuff(buff), new EffectSource(this), skillObj, DateTime.UtcNow);
                }
            }
        }
    }

    public void Dispel(BaseUnit caster, BaseUnit owner, Buff buff, bool replaced = false)
    {
        foreach (var template in Bonuses)
            owner.RemoveBonus(buff.Index, template.Attribute);
        var requiringBuffs = owner.Buffs.GetBuffsRequiring(buff.Template.Id);
        foreach (var requiringBuff in requiringBuffs.ToList())
            requiringBuff.Exit();

        if (!buff.Passive && !replaced)
            owner.BroadcastPacket(new SCBuffRemovedPacket(owner.ObjId, buff.Index), true);

        // Special properties handling
        if (owner is Character character)
        {
            if (FindSchoolOfFishRange > 0)
                RadarManager.Instance.RegisterForFishSchool(character, 0f);
            if (TransferTelescopeRange > 0)
                RadarManager.Instance.RegisterForPublicTransport(character, 0f);
            if (TelescopeRange > 0)
                RadarManager.Instance.RegisterForShips(character, 0f);
        }
    }

    public void WriteData(PacketStream stream, uint abLevel)
    {
        stream.WritePisc(0, GetDuration(abLevel) / 10, 0, (long)(Tick / 10)); // unk, Duration, unk / 10, Tick
    }

    public int GetDuration(uint abLevel)
    {
        return Math.Max(0, (LevelDuration * (int)abLevel) + Duration);
    }

    public double GetTick()
    {
        return Tick;
    }
}
