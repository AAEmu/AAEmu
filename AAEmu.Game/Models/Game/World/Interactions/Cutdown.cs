using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions
{

    public class Cutdown : IWorldInteraction
    {
        public void Execute(Unit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType, uint skillId, uint doodadId, DoodadFuncTemplate objectFunc)
        {
            if (target is Doodad doodad)
            {
                // DoodadManager.Instance.TriggerFunc(GetType().Name, caster, doodad, skillId);
                doodad.Use(caster, skillId);
                caster.BroadcastPacket(new SCVegetationCutdowningPacket(caster.ObjId, doodad.ObjId), true);
            }
        }
    }
}
