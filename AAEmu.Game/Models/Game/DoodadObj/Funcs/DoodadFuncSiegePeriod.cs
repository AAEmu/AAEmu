using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncSiegePeriod : DoodadFuncTemplate
    {
        public uint SiegePeriodId { get; set; }
        public uint NextPhase { get; set; }
        public bool Defense { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncSiegePeriod");
        }
    }
}
