using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Skills.Effects.SpecialEffects;

public class RemoveDoodad : SpecialEffectAction
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
        if (caster is Character) { Logger.Debug("Special effects: RemoveDoodad value1 {0}, value2 {1}, value3 {2}, value4 {3}", value1, value2, value3, value4); }

        var doodads = WorldManager.GetAround<Doodad>(caster, 10f);
        if (doodads != null)
            foreach (var doodad in doodads)
                if (doodad.TemplateId == value1)
                {
                    doodad.Delete();
                    doodad.BroadcastPacket(new SCDoodadRemovedPacket(doodad.ObjId), false);
                }
    }
}
