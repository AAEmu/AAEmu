using System;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class SavePortal : SpecialEffectAction
{
    public override void Execute(BaseUnit caster,
        SkillCaster casterObj,
        BaseUnit target,
        SkillCastTarget targetObj,
        CastAction castObj,
        Skill skill,
        SkillObject skillObject,
        DateTime time,
        int value1,
        int value2,
        int value3,
        int value4)
    {
        // TODO ...
        if (caster is Character character)
        {
            Logger.Debug("Special effects: SavePortal value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4);

            if (skillObject is SkillObjectUnk2 so)
            {
                if (so.Id == 0)
                {
                    character.Portals.AddPrivatePortal(character.Transform.World.Position.X,
                        character.Transform.World.Position.Y, character.Transform.World.Position.Z,
                        character.Transform.World.Rotation.Z, character.Transform.ZoneId, so.Name);
                }
                else
                {
                    character.Portals.ChangePrivatePortalName((uint)so.Id, so.Name);
                }
            }
        }
    }
}
