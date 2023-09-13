using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncNaviRemoveTimer : DoodadPhaseFuncTemplate
    {
        public int After { get; set; }

        public override bool Use(BaseUnit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncNaviRemoveTimer");
            return false;
        }
    }
}
