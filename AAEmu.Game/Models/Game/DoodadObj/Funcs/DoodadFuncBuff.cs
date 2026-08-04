using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncBuff : DoodadFuncTemplate
{
    // doodad_funcs
    public uint BuffId { get; set; }
    public float Radius { get; set; }
    public int Count { get; set; }
    public uint PermId { get; set; } // Unused
    public uint RelationshipId { get; set; }

    public override void Use(BaseUnit caster, Doodad owner, uint skillId, int nextPhase = 0)
    {
        Logger.Trace("DoodadFuncBuff");
        if (Radius <= 0f)
        {
            // Caster only
            caster.Buffs.AddBuff(BuffId, caster);
        }
        else
        {
            var relationship = (RelationState)RelationshipId;
            var targets = WorldManager
                .GetAround<BaseUnit>(caster, Radius, true)
                .Where(target => target != null && caster.GetRelationStateTo(target) == relationship)
                .Take(Count);
            foreach (var target in targets)
            {
                target.Buffs.AddBuff(BuffId, caster);
            }
        }
    }
}
