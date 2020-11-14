using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class KillNpcWithoutCorpseEffect : EffectTemplate
    {
        public uint NpcId { get; set; }
        public float Radius { get; set; }
        public bool GiveExp { get; set; }
        public bool Vanish { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Debug("KillNpcWithoutCorpseEffect");
            var npcs = WorldManager.Instance.GetAround<Npc>(target, Radius);
            foreach (var npc in npcs)
            {
                if (npc.TemplateId != NpcId)
                    continue;
                npc.Effects.RemoveAllEffects();
                npc.Delete();
            }
        }
    }
}
