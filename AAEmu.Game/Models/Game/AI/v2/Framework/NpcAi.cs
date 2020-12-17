using System;
using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using NLog;

namespace AAEmu.Game.Models.Game.AI.v2
{
    /// <summary>
    /// This is the basics of a unit's AI: The state machine. It also carries data about which unit owns it
    /// </summary>
    public abstract class NpcAi
    {
        private Logger _log = LogManager.GetCurrentClassLogger();
        
        public Npc Owner { get; set; }
        public Point IdlePosition { get; set; }

        private Dictionary<BehaviorKind, Behavior> _behaviors;
        private Dictionary<Behavior, List<Transition>> _transitions;
        private Behavior _currentBehavior;

        public NpcAi()
        {
            _behaviors = new Dictionary<BehaviorKind, Behavior>();
            _transitions = new Dictionary<Behavior, List<Transition>>();
        }

        public void Start()
        {
            Build();
            CheckValid();
        }
        
        protected abstract void Build();

        private void CheckValid()
        {
            foreach (var transition in _transitions.Values.SelectMany(transitions => transitions.Where(transition => !_behaviors.ContainsValue(transition.Target))))
            {
                _log.Error("Transition is invalid. Type {0} missing, while used in transition on {1}",
                    transition.Target.GetType().Name, transition.On);
            }
        }

        protected Behavior AddBehavior(BehaviorKind kind, Behavior behavior)
        {
            _behaviors.Add(kind, behavior);
            return behavior;
        }

        public Behavior GetBehavior(BehaviorKind kind)
        {
            return _behaviors[kind];
        }

        private void SetCurrentBehavior(Behavior behavior)
        {
            _currentBehavior?.Exit();
            _currentBehavior = behavior;
            _currentBehavior?.Enter();
        }

        protected void SetCurrentBehavior(BehaviorKind kind)
        {
            if (_behaviors.ContainsKey(kind))
            {
                _log.Warn("Trying to set current behavior, but it is not valid. Missing behavior: {0}", kind);
                return;
            }
            
            SetCurrentBehavior(_behaviors[kind]);
        }

        public Behavior AddTransition(Behavior source, Transition target)
        {
            if (!_transitions.ContainsKey(source))
                _transitions.Add(source, new List<Transition>());
            _transitions[source].Add(target);
            return source;
        }

        public void Tick(TimeSpan delta)
        {
            _currentBehavior?.Tick(delta);
        }
        
        private void Transition(TransitionEvent on)
        {
            if (!_transitions.ContainsKey(_currentBehavior))
                return;
            var transition = _transitions[_currentBehavior].SingleOrDefault(t => t.On == on);
            if (transition == null)
                return;
            
            var newBehavior = transition.Target;
            SetCurrentBehavior(newBehavior);
        }
        
        #region Events
        public void OnNoAggroTarget()
        {
            Transition(TransitionEvent.OnNoAggroTarget);
        }
        #endregion
        
        /// <summary>
        /// These appear to be ways to force a state change, ignoring existing transitions. 
        /// </summary>
        #region Go to X
        public virtual void GoToSpawn()
        {
            SetCurrentBehavior(BehaviorKind.Spawning);
        }
        
        public virtual void GoToIdle()
        {
            SetCurrentBehavior(BehaviorKind.Idle);
        }
        
        public virtual void GoToRunCommandSet()
        {
            SetCurrentBehavior(BehaviorKind.RunCommandSet);
        }
        
        public virtual void GoToTalk()
        {
            SetCurrentBehavior(BehaviorKind.Talk);
        }
        
        public virtual void GoToAlert()
        {
            SetCurrentBehavior(BehaviorKind.Alert);
        }
        
        public virtual void GoToCombat()
        {
            SetCurrentBehavior(BehaviorKind.Attack);
        }
        public virtual void GoToFollowPath()
        {
            SetCurrentBehavior(BehaviorKind.FollowPath);
        }
        
        public virtual void GoToFollowUnit()
        {
            SetCurrentBehavior(BehaviorKind.FollowUnit);
        }
        
        public virtual void GoToReturn()
        {
            SetCurrentBehavior(BehaviorKind.ReturnState);
        }
        
        public virtual void GoToDead()
        {
            SetCurrentBehavior(BehaviorKind.Dead);
        }
        
        public virtual void GoToDespawn()
        {
            SetCurrentBehavior(BehaviorKind.Despawning);
        }
        #endregion
    }
}
