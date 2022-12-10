using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncExchange : DoodadPhaseFuncTemplate
    {
        public override bool Use(BaseUnit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncExchange");
            return false;
        }
    }
}
