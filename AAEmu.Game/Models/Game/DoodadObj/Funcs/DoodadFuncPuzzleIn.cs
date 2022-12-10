using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncPuzzleIn : DoodadPhaseFuncTemplate
    {
        public uint GroupId { get; set; }
        public float Ratio { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncPuzzleIn");
            return false;
        }
    }
}
