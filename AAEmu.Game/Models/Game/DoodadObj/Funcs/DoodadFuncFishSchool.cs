using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs;

public class DoodadFuncFishSchool : DoodadPhaseFuncTemplate
{
    public uint NpcSpawnerId { get; set; }

    public override bool Use(BaseUnit caster, Doodad owner)
    {
        Logger.Trace($"DoodadFuncFishSchool NpcSpawnerId={NpcSpawnerId}");

        //No initial spawn, fish spawn are triggered by fishing.

        return false;
    }
}
