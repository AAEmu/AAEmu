using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncSpawnMgmt : DoodadFuncTemplate
    {
        public uint GroupId { get; set; }
        public bool Spawn { get; set; }
        public uint ZoneId { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncSpawnMgmt");
        }
    }
}
