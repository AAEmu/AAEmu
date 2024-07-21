using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncZoneReact : DoodadPhaseFuncTemplate
{
    public uint ZoneGroupId { get; set; }
    public int NextPhase { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace("DoodadFuncZoneReact");
        // Triggers if the owner is inside the specified zone
        var zoneGroup = ZoneManager.Instance.GetZoneByKey(owner.Transform.ZoneId);
        if (zoneGroup?.GroupId == ZoneGroupId)
        {
            owner.OverridePhase = NextPhase;
            return true;
        }

        return false;
    }
}
