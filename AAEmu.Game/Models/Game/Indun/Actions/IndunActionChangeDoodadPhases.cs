using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Models.Game.DoodadObj;

using InstanceWorld = AAEmu.Game.Models.Game.World.World;

namespace AAEmu.Game.Models.Game.Indun.Actions;

internal class IndunActionChangeDoodadPhases : IndunAction
{
    public uint DoodadAlmightyId { get; set; }
    public uint DoodadFuncGroupId { get; set; }

    public override void Execute(InstanceWorld world)
    {
            foreach (var doodad in GetDoodads(world))
            {
                doodad.DoChangePhase(null, (int)DoodadFuncGroupId);
            }
            Logger.Warn("IndunActionChangeDoodadPhases: Doodad " +DoodadAlmightyId + " change phase to " + DoodadFuncGroupId);
        }

    private List<Doodad> GetDoodads(InstanceWorld world)
    {

            var doodadList = new List<Doodad>();

            foreach (var region in world.Regions)
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