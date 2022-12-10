using System.Collections.Generic;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Game.World.Transform;

namespace AAEmu.Game.Models.Game.AI.Framework
{
    /// <summary>
    /// Any generic AI
    /// </summary>
    public abstract class AbstractAI
    {
        public GameObject Owner { get; set; }
        public PositionAndRotation IdlePosition { get; set; } = new PositionAndRotation();
        public FSM StateMachine { get; set; } = new FSM();
        public abstract States GetNextState(State previous);
    }
}
