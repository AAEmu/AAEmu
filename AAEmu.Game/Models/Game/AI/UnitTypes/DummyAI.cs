using AAEmu.Game.Models.Game.AI.Framework;

namespace AAEmu.Game.Models.Game.AI.UnitTypes
{
    public class DummyAI : AbstractUnitAI
    {
        public override Framework.States GetNextState(State previous)
        {
            return Framework.States.Idle;
        }
    }
}
