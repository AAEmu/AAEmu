using System;

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects
{
    public class SpawnDoodad : SpecialEffectAction
    {
        protected override SpecialType SpecialEffectActionType => SpecialType.SpawnDoodad;

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
            // TODO ...
            if (caster is Character) { _log.Debug("Special effects: SpawnDoodad doodadId {0}, value2 {1}, value3 {2}, value4 {3}", doodadId, value2, value3, value4); }

            var doodad = DoodadManager.Instance.Create(0, (uint)doodadId, caster);
            doodad.Transform = caster.Transform.CloneDetached(doodad);
            var rpy = target.Transform.World.ToRollPitchYawDegrees();
            var (xx, yy) = MathUtil.AddDistanceToFrontDeg(1f, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, rpy.Z + 90f); //  + 90f to Front
            doodad.SetPosition(xx, yy, target.Transform.World.Position.Z, rpy.X, rpy.Y, rpy.Z);
            if (AppConfiguration.Instance.HeightMapsEnable)
                doodad.Transform.Local.SetHeight(WorldManager.Instance.GetHeight(doodad.Transform.ZoneId, xx, yy));

            doodad.Spawn();
        }
    }
}
