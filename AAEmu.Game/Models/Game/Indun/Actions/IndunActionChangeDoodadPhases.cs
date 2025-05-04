using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

internal class IndunActionChangeDoodadPhases : IndunAction
{
    public uint DoodadAlmightyId { get; set; }
    public uint DoodadFuncGroupId { get; set; }

    public override void Execute(WorldInstance worldInstance)
    {
        foreach (var doodad in GetDoodads(worldInstance))
        {
            doodad.DoChangePhase(null, (int)DoodadFuncGroupId);
        }
        Logger.Warn("IndunActionChangeDoodadPhases: Doodad " + DoodadAlmightyId + " change phase to " + DoodadFuncGroupId);
    }

    private List<Doodad> GetDoodads(WorldInstance worldInstance)
    {

        var doodadList = new List<Doodad>();

        foreach (var region in worldInstance.Regions)
        {
            region.GetList(doodadList, 0);
        }
        doodadList = doodadList.Where(doodad => doodad.TemplateId == DoodadAlmightyId).ToList();
        if (doodadList.Count > 0)
        {
            return doodadList;
        }

        Logger.Warn(DoodadAlmightyId + " is not found!");
        return doodadList;
    }
}
