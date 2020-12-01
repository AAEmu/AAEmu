using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ManaBurnEffect : EffectTemplate
    {
        public int BaseMin { get; set; }
        public int BaseMax { get; set; }
        public int DamageRatio { get; set; }
        public float LevelMd { get; set; }
        public int LevelVaStart { get; set; }
        public int LevelVaEnd { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("ManaBurnEffect");
            var min = 0.0f;
            var max = 0.0f;
            
            min += BaseMin;
            max += BaseMax;

            var levelMin = 0.0f;
            var levelMax = 0.0f;
            
            var lvlMd = caster.LevelDps * LevelMd;
            // Hack null-check on skill
            var levelModifier = (((source.Skill?.Level ?? 1) - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;
            
            min += (lvlMd - levelModifier * lvlMd) + 0.5f;
            max += (levelModifier + 1) * lvlMd + 0.5f;
            
            if (source.Buff?.TickEffects.Count > 0)
            {
                min = (float) (min * (source.Buff.Tick / source.Buff.Duration));
                max = (float) (max * (source.Buff.Tick / source.Buff.Duration));
            }

            var finalDamage = Rand.Next(min, max);

            if (target is Unit targetUnit)
            {
                targetUnit.ReduceCurrentMp(caster, (int) finalDamage);
                var packet = new SCUnitDamagedPacket(castObj, casterObj, caster.ObjId, target.ObjId, 0, 0)
                {
                    _manaBurn = (int) finalDamage
                };
                target.BroadcastPacket(packet, true);
            }
        }
    }
}
