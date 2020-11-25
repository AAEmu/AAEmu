using System;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class BuffEffect : EffectTemplate
    {
        public int Chance { get; set; }
        public int Stack { get; set; }
        public int AbLevel { get; set; }
        public BuffTemplate Buff { get; set; }
        public override uint BuffId => Buff.BuffId;
        public override bool OnActionTime => Buff.Tick > 0;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            if (target is Unit trg)
            {
                var hitType = SkillHitType.Invalid;
                if ((source.Skill?.HitTypes.TryGetValue(trg.ObjId, out hitType) ?? false)
                    && (source.Skill?.SkillMissed(trg.ObjId) ?? false))
                {
                    return;
                }
            }
            if (Rand.Next(0, 101) > Chance)
                return;
            if (Buff.RequireBuffId > 0 && !target.Effects.CheckBuff(Buff.RequireBuffId))
                return; // TODO send error?
            if (target.Effects.CheckBuffImmune(Buff.Id))
                return; // TODO send error of immune?

            uint abLevel = 1;
            if (caster is Character character)
            {
                if (source.Skill != null)
                {
                    var template = source.Skill.Template;
                    var abilityLevel = character.GetAbLevel((AbilityType)source.Skill.Template.AbilityId);
                    if (template.LevelStep != 0)
                        abLevel = (uint)((abilityLevel / template.LevelStep) * template.LevelStep);
                    else
                        abLevel = (uint)template.AbilityLevel;
                }
                else if (source.Buff != null)
                {
                    //not sure?
                }
            }

            //Safeguard to prevent accidental flagging
            if (Buff.Kind == BuffKind.Bad && !caster.CanAttack(target) && caster != target)
                return;
            target.Effects.AddEffect(new Effect(target, caster, casterObj, this, source.Skill, time) { AbLevel = abLevel });
            
            if (Buff.Kind == BuffKind.Bad && caster.GetRelationStateTo(target) == RelationState.Friendly 
                && caster != target && !target.Effects.CheckBuff((uint)BuffConstants.RETRIBUTION_BUFF))
            {
                caster.SetCriminalState(true);
            }
        }

        public override void Start(Unit caster, BaseUnit owner, Effect effect)
        {
            foreach (var template in Buff.Bonuses)
            {
                var bonus = new Bonus();
                bonus.Template = template;
                bonus.Value = (int) Math.Round(template.Value + (template.LinearLevelBonus * (effect.AbLevel / 100f)));
                owner.AddBonus(effect.Index, bonus);
            }

            effect.Charge = Rand.Next(Buff.InitMinCharge, Buff.InitMaxCharge);
            if (!effect.Passive)
                owner.BroadcastPacket(new SCBuffCreatedPacket(effect), true);
        }

        public override void TimeToTimeApply(Unit caster, BaseUnit owner, Effect effect)
        {
            if (Buff.TickEffect == null)
                return;
            if (Buff.TickEffect.TargetBuffTagId > 0 &&
                !owner.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(Buff.TickEffect.TargetBuffTagId)))
                return;
            if (Buff.TickEffect.TargetNoBuffTagId > 0 &&
                owner.Effects.CheckBuffs(SkillManager.Instance.GetBuffsByTagId(Buff.TickEffect.TargetNoBuffTagId)))
                return;
            var eff = SkillManager.Instance.GetEffectTemplate(Buff.TickEffect.EffectId);
            var targetObj = new SkillCastUnitTarget(owner.ObjId);
            var skillObj = new SkillObject(); // TODO ?
            eff.Apply(caster, effect.SkillCaster, owner, targetObj, new CastBuff(effect), new EffectSource(Buff), skillObj, DateTime.Now);
        }

        public override void Dispel(Unit caster, BaseUnit owner, Effect effect, bool replaced = false)
        {
            foreach (var template in Buff.Bonuses)
                owner.RemoveBonus(effect.Index, template.Attribute);
            if (!effect.Passive && !replaced)
                owner.BroadcastPacket(new SCBuffRemovedPacket(owner.ObjId, effect.Index), true);
        }

        public override void WriteData(PacketStream stream, uint abLevel)
        {
            stream.WritePisc(0, GetDuration(abLevel) / 10, 0, (long)(Buff.Tick / 10)); // TODO unk, Duration / 10, unk / 10, Tick / 10
        }

        public override int GetDuration(uint abLevel)
        {
            return (Buff.LevelDuration * (int)abLevel) + Buff.Duration;
        }

        public override double GetTick()
        {
            return Buff.Tick;
        }
    }
}
