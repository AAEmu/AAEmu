using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncSpawnMgmt : DoodadPhaseFuncTemplate
    {
        public uint GroupId { get; set; }
        public bool Spawn { get; set; }
        public uint ZoneId { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncSpawnMgmt");
            return false;
        }
    }
}
