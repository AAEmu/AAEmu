using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.World.Interactions;

public class SummonDoodad : IWorldInteraction
{
    public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
        uint skillId, uint doodadId, DoodadFuncTemplate objectFunc = null)
        => Execute(caster, casterType, target, targetType, skillId, doodadId, objectFunc, 0, 0, 0);

    public void Execute(BaseUnit caster, SkillCaster casterType, BaseUnit target, SkillCastTarget targetType,
        uint skillId, uint doodadId, DoodadFuncTemplate objectFunc, ushort castTlId, uint plotId, uint plotEventId)
    {
        var doodad = DoodadManager.Instance.Create(caster.ParentWorld, 0, doodadId, caster, true);
        if (doodad == null)
        {
            return;
        }
        doodad.OwnerSkillId = skillId;
        doodad.OwnerCastTlId = castTlId;
        doodad.OwnerPlotId = plotId;
        doodad.OwnerPlotEventId = plotEventId;
        doodad.Transform = target.Transform.CloneDetached(doodad);
        doodad.InitDoodad();
        doodad.Spawn();
    }
}
