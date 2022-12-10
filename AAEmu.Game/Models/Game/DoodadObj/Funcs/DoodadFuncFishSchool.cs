using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncFishSchool : DoodadPhaseFuncTemplate
    {
        public uint NpcSpawnerId { get; set; }
        
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncFishSchool");
            return false;
        }
    }
}
