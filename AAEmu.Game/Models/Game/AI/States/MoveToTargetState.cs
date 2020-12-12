using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class MoveToTargetState : State
    {
        public Unit Target { get; set; }
    }
}
