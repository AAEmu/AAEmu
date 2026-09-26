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
                    if (!character.Portals.TryAddPrivatePortal(character.Transform.World.Position.X,
                            character.Transform.World.Position.Y, character.Transform.World.Position.Z,
                            character.Transform.World.Rotation.Z, character.Transform.ZoneId, so.Name, out _))
                    {
                        Logger.Warn("SavePortal refused an invalid private portal name");
                    }
                }
                else if (so.Id > 0)
                {
                    if (!character.Portals.ChangePrivatePortalName((uint)so.Id, so.Name))
                        Logger.Warn("SavePortal could not rename private portal {0}", so.Id);
                }
                else
                {
                    Logger.Warn("SavePortal received a negative private portal id {0}", so.Id);
                }
            }
        }
    }
}
