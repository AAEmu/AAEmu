using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncWaterVolume : DoodadPhaseFuncTemplate
    {
        public float LevelChange { get; set; }
        public float Duration { get; set; }
        
        public override bool Use(Unit caster, Doodad owner)
        {
            _log.Trace("DoodadFuncWaterVolume");
            return false;
        }
    }
}
