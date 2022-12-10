using AAEmu.Game.Models.Game.DoodadObj.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.DoodadObj.Funcs
{
    public class DoodadFuncAnimate : DoodadPhaseFuncTemplate
    {
        // doodad_phase_funcs
        public string Name { get; set; }
        public bool PlayOnce { get; set; }

        public override bool Use(Unit caster, Doodad owner)
        {
            if (!PlayOnce)
            {
                //The client might handle this flag already
            }

            return false;
        }
    }
}
