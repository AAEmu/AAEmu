using System;
using System.Linq;

using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Char;
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

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj, CastAction castObj,
            EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Trace("KillNpcWithoutCorpseEffect");

            if (caster is Character) { return; } // does not apply to the character
            if (Vanish && Radius == 0)
            {
                // Fixed: "Trainer Daru" disappears after selling a bear
                RemoveEffectsAndDelete(caster);
            }
            else
            {
                var npcs = WorldManager.Instance.GetAround<Npc>(target, Radius);
                if (npcs == null) { return; }
                foreach (var npc in npcs.Where(npc => npc.TemplateId == NpcId))
                {
                    RemoveEffectsAndDelete(caster);
                }
            }
        }

        private void RemoveEffectsAndDelete(Unit unit)
        {
            unit.Buffs.RemoveAllEffects();
            if (unit is Npc npc && npc.Spawner != null)
            {
                npc.Spawner.DespawnWithRespawn(npc);
            }
        }
    }
}
