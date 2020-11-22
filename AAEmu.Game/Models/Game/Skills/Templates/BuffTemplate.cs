using System;
using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Templates
{
    public class BuffTemplate : EffectTemplate
    {
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
        public uint StackRuleId { get; set; }
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
        public float TickAuraRadius { get; set; }
        public uint TickAreaRelationId { get; set; }
        public bool RemoveOnMove { get; set; }
        public bool UseSourceFaction { get; set; }
        public uint FactionId { get; set; }
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
        public float FindSchoolOrFishRange { get; set; }
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
        public override bool OnActionTime => Tick > 0;

        public TickEffect TickEffect { get; set; }
        public List<BonusTemplate> Bonuses { get; set; }
        public List<DynamicBonusTemplate> DynamicBonuses { get; set; }

        public BuffTemplate()
        {
            Bonuses = new List<BonusTemplate>();
            DynamicBonuses = new List<DynamicBonusTemplate>();
        }

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            if (RequireBuffId > 0 && !target.Effects.CheckBuff(RequireBuffId))
                return; //TODO send error?
            if (target.Effects.CheckBuffImmune(Id))
                return; //TODO  error of immune?
            target.Effects.AddEffect(new Effect(target, caster, casterObj, this, source?.Skill, time));
        }

        public override void Start(Unit caster, BaseUnit owner, Effect effect)
        {
            foreach (var template in Bonuses)
            {
                var bonus = new Bonus();
                bonus.Template = template;
                bonus.Value = (int) (template.Value + (template.LinearLevelBonus * (effect.AbLevel / 100)));
                owner.AddBonus(effect.Index, bonus);
            }

            if (effect.Charge == 0)
                effect.Charge = Rand.Next(InitMinCharge, InitMaxCharge);
            
            if (!effect.Passive)
                owner.BroadcastPacket(new SCBuffCreatedPacket(effect), true);
        }

        public override void TimeToTimeApply(Unit caster, BaseUnit owner, Effect effect)
        {
            if (TickEffect != null)
            {
                if (TickEffect.TargetBuffTagId > 0 &&
                    !owner.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(TickEffect.TargetBuffTagId)))
                    return;
                if (TickEffect.TargetNoBuffTagId > 0 &&
                    owner.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(TickEffect.TargetNoBuffTagId)))
                    return;
                var eff = SkillManager.Instance.GetEffectTemplate(TickEffect.EffectId);
                var targetObj = new SkillCastUnitTarget(owner.ObjId);
                var skillObj = new SkillObject(); // TODO ?
                eff.Apply(caster, effect.SkillCaster, owner, targetObj, new CastBuff(effect), null, skillObj, DateTime.Now);
            }
        }

        public override void Dispel(Unit caster, BaseUnit owner, Effect effect, bool replaced = false)
        {
            foreach (var template in Bonuses)
                owner.RemoveBonus(effect.Index, template.Attribute);
            if (!effect.Passive && !replaced)
                owner.BroadcastPacket(new SCBuffRemovedPacket(owner.ObjId, effect.Index), true);
        }

        public override void WriteData(PacketStream stream)
        {
            stream.WritePisc(0, Duration / 10, 0, (long) (Tick / 10)); // unk, Duration, unk / 10, Tick
        }

        public override int GetDuration()
        {
            return Duration;
        }

        public override double GetTick()
        {
            return Tick;
        }
    }
}
