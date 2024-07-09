using System;

using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SpawnDoodad : SpecialEffectAction
{
    protected override SpecialType SpecialEffectActionType => SpecialType.SpawnDoodad;

    public override void Execute(BaseUnit caster,
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
        int value4
    )
    {
        if (caster is Character)
        {
            Logger.Debug($"Special effects: SpawnDoodad doodadId {doodadId}, value2 {value2}, value3 {value3}, value4 {value4}");
        }

        var doodad = DoodadManager.Instance.Create(0, (uint)doodadId, caster, true);

        doodad.Transform = caster.Transform.CloneDetached(doodad);
        var rpy = target.Transform.World.ToRollPitchYawDegrees();
        switch (skill.Template.TargetSelection)
        {
            case SkillTargetSelection.Source:
                doodad.Transform = caster.Transform.CloneDetached(doodad);
                break;
            case SkillTargetSelection.Target :
                doodad.Transform = target.Transform.CloneDetached(doodad);
                break;
            //case SkillTargetSelection.Line:
            //    break;
            //case SkillTargetSelection.Location:
            //    break;
            default:
                doodad.Transform = caster.Transform.CloneDetached(doodad);
                break;
        }
        var (xx, yy) = MathUtil.AddDistanceToFrontDeg(1f, doodad.Transform.World.Position.X, doodad.Transform.World.Position.Y, rpy.Z + 90f); //  + 90f to Front
        doodad.SetPosition(xx, yy, WorldManager.Instance.GetHeight(doodad.Transform), rpy.X, rpy.Y, rpy.Z);
        doodad.InitDoodad();
        doodad.Spawn();
    }
}
