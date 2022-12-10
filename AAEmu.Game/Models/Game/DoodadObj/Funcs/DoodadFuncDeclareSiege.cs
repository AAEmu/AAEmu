using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncDeclareSiege : DoodadPhaseFuncTemplate
    {
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncDeclareSiege");
            return false;
        }
    }
}
