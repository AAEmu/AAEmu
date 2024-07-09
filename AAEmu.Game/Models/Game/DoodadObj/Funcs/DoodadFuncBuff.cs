using AAEmu.Game.Core.Managers.World;
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
        // TODO: ImplementRelationShipId
        // TODO: Not sure what count is, maximum targets maybe?
        
        if (Radius <= 0f)
        {
            // Caster only
            caster.Buffs.AddBuff(BuffId, caster);
        }
        else
        {
            var targets = WorldManager.GetAround<BaseUnit>(caster, Radius, true);
            foreach (var target in targets)
            {
                target.Buffs.AddBuff(BuffId, caster);
            }
        }
    }
}
