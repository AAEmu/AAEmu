using System;
using System.Collections.Generic;
using NLog;

namespace AAEmu.Game.Models.Game.AI.Framework
{
    public enum States
    {
        Idle = 0,
        Roaming = 1,
        MovingToTarget = 2,
        UsingCombatSkills = 3,
        AlmightyAttack = 4,
        ReturnToIdle = 5,
        BigMonsterAttack = 6
    }
    
    public class State
    {
        protected static Logger _log = LogManager.GetCurrentClassLogger();
        public AbstractAI AI;
        public virtual void Enter() { }
        public virtual void Exit() { }
        public virtual void Tick(TimeSpan delta) { }
    }

    public class FSM
    {
        protected Dictionary<States, State> _states = new Dictionary<States, State>();
        protected State _currentState;
        
        public FSM() {}

        public void Tick(TimeSpan delta)
        {
            _currentState?.Tick(delta);
        }

        public State GetCurrentState()
        {
            return _currentState;
        }
        
        public void SetCurrentState(State state)
        {
            _currentState?.Exit();
            _currentState = state;
            _currentState?.Enter();
        }

        public void AddState(States key, State state)
        {
            _states.Add(key, state);
        }

        public State GetState(States key)
        {
            return _states.TryGetValue(key, out var state) ? state : null;
        }
    }
}
