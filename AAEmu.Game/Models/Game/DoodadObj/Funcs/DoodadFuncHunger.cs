using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncHunger : DoodadFuncTemplate
    {
        public int HungryTerm { get; set; }
        public int FullStep { get; set; }
        public int PhaseChangeLimit { get; set; }
        public uint NextPhase { get; set; }
        
        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Debug("DoodadFuncHunger");
        }
    }
}
