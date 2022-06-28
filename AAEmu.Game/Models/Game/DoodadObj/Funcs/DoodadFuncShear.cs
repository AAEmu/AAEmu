using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncShear : DoodadFuncTemplate
    {
        // doodad_funcs
        public uint ShearTypeId { get; set; }
        public int ShearTerm { get; set; }

        public override void Use(Unit caster, Doodad owner, uint skillId, int nextPhase = 0)
        {
            _log.Trace("DoodadFuncShear");
        }
    }
}
