using System.Collections.Generic;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.AI.v2
{
    /// <summary>
    /// This is the basics of a unit's AI: The state machine. It also carries data about which unit owns it
    /// </summary>
    public abstract class NpcAi
    {
        public Npc Owner { get; set; }
        public Point IdlePosition { get; set; }

        private List<Behavior> _behaviors;
        private Dictionary<Behavior, List<Transition>> _transitions;

        public abstract void Build();

        protected Behavior AddBehavior(Behavior behavior)
        {
            _behaviors.Add(behavior);
            return behavior;
        }

        public Behavior GetBehavior(BehaviorKind kind)
        {
            return null; // TODO : Return the corresponding behavior
        }

        public Behavior AddTransition(Behavior source, Transition target)
        {
            if (!_transitions.ContainsKey(source))
                _transitions.Add(source, new List<Transition>());
            _transitions[source].Add(target);
            return source;
        }
        
        #region Events

        #endregion
    }
}
