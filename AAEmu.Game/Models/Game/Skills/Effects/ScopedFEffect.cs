using System;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Skills.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects
{
    public class ScopedFEffect : EffectTemplate
    {
        public int Range { get; set; }
        public bool Key { get; set; }
        public uint DoodadId { get; set; }

        public override bool OnActionTime => false;

        public override void Apply(Unit caster, SkillCaster casterObj, BaseUnit target, SkillCastTarget targetObj,
            CastAction castObj, EffectSource source, SkillObject skillObject, DateTime time, CompressedGamePackets packetBuilder = null)
        {
            _log.Trace("ScopedFEffect");
            if (caster is not Character character) { return; }

            var doodads = WorldManager.Instance.GetAround<Doodad>(character, Range/1000f);
            foreach (var doodad in doodads)
            {
                if (doodad.TemplateId != DoodadId) { continue; }

                doodad.Use(caster, source.Skill.Id);
                break;
            }
        }
    }
}
