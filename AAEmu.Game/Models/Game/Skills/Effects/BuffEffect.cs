using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
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

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            Skill skill, SkillObject skillObject, DateTime time)
        {
            if (Buff.RequireBuffId > 0 && !target.Effects.CheckBuff(Buff.RequireBuffId))
                return; // TODO send error?
            if (target.Effects.CheckBuffImmune(Buff.Id))
                return; // TODO send error of immune?
            target.Effects.AddEffect(new Effect(target, caster, casterObj, this, skill, time));
        }

        public override void Start(Unit caster, BaseUnit owner, Effect effect)
        {
            foreach (var template in Buff.Bonuses)
            {
                var bonus = new Bonus();
                bonus.Template = template;
                bonus.Value = template.Value; // TODO using LinearLevelBonus
                owner.AddBonus(effect.Index, bonus);
            }

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
            eff.Apply(caster, effect.SkillCaster, owner, targetObj, new CastBuff(effect), null, skillObj, DateTime.Now);
        }

        public override void Dispel(Unit caster, BaseUnit owner, Effect effect)
        {
            foreach (var template in Buff.Bonuses)
                owner.RemoveBonus(effect.Index, template.Attribute);
            owner.BroadcastPacket(new SCBuffRemovedPacket(owner.ObjId, effect.Index), true);
        }

        public override void WriteData(PacketStream stream)
        {
            stream.WritePisc(0, Buff.Duration / 10, 0, (long)(Buff.Tick / 10)); // TODO unk, Duration / 10, unk / 10, Tick / 10
        }

        public override int GetDuration()
        {
            return Buff.Duration;
        }

        public override double GetTick()
        {
            return Buff.Tick;
        }
    }
}
