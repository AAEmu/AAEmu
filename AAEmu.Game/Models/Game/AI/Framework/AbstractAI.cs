using System.Collections.Generic;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.AI.Framework
{
    /// <summary>
    /// Any generic AI
    /// </summary>
    public abstract class AbstractAI
    {
        public GameObject Owner { get; set; }
        public Point IdlePosition { get; set; }
        public FSM StateMachine { get; set; } = new FSM();
        public abstract States GetNextState(State previous);
    }
}
