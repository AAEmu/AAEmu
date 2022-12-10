using System;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class AggroEffect : EffectTemplate
    {
        public bool UseFixedAggro { get; set; }
        public int FixedMin { get; set; }
        public int FixedMax { get; set; }
        public bool UseLevelAggro { get; set; }
        public float LevelMd { get; set; }
        public int LevelVaStart { get; set; }
        public int LevelVaEnd { get; set; }
        public bool UseChargedBuff { get; set; }
        public uint ChargedBuffId { get; set; }
        public float ChargedMul { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            if (!(caster is Character character))
                return;

            if (!(target is Npc npc))
                return;

            _log.Debug("AggroEffect");

            var min = 0.0f;
            var max = 0.0f;

            if (UseLevelAggro)
            {
                var lvlMd = caster.LevelDps * LevelMd;
                var levelModifier = (((source.Skill?.Level ?? 1) - 1) / 49 * (LevelVaEnd - LevelVaStart) + LevelVaStart) * 0.01f;

                min += lvlMd - levelModifier * lvlMd + 0.5f;
                max += (levelModifier + 1) * lvlMd + 0.5f;
            }

            if (UseChargedBuff)
            {
                var effect = caster.Buffs.GetEffectFromBuffId(ChargedBuffId);
                if (effect != null)
                {
                    min += ChargedMul * effect.Charge;
                    max += ChargedMul * effect.Charge;
                    effect.Exit();
                }
            }

            if (UseFixedAggro)
            {
                min += FixedMin;
                max += FixedMax;
            }

            var value = (int)Rand.Next(min, max);
            npc.BroadcastPacket(new SCAiAggroPacket(npc.ObjId, 1, caster.ObjId, value), true);
            npc.AddUnitAggro(AggroKind.Damage, character, value);
        }
    }
}
