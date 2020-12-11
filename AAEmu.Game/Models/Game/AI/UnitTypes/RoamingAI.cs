using AAEmu.Game.Models.Game.AI.Framework;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class RoamingAI : AbstractUnitAI
    {
        /*
         * The roaming AI has 4 goals:
         * - Move to target when in combat
         * - Use skills when in combat
         * - Roam when not in combat
         *
         * The roaming should also work when working with formations, that is, pack of mobs walking together.
         */
    }
}
