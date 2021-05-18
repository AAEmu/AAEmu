using System;

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class SpawnDoodad : SpecialEffectAction
    {
        public override void Execute(Unit caster,
            SkillCaster casterObj,
            BaseUnit target,
            SkillCastTarget targetObj,
            CastAction castObj,
            Skill skill,
            SkillObject skillObject,
            DateTime time,
            int doodadId,
            int value2, // sometimes 1000
            int value3,
            int value4)
        {
            var doodad = DoodadManager.Instance.Create(0, (uint)doodadId, caster);
            doodad.Position = caster.Position.Clone();
            doodad.Spawn();
        }
    }
}
