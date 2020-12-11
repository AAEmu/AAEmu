using System;
using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.AI.Framework
{
    public class State
    {
        protected AbstractAI AI;
        public virtual void Enter() { }
        public virtual void Exit() { }
        public virtual void Tick(TimeSpan delta) { }
    }

    public class FSM
    {
        protected Dictionary<uint, State> _states = new Dictionary<uint, State>();
        protected State _currentState;
        
        public FSM() {}

        public void Tick(TimeSpan delta)
        {
            _currentState?.Tick(delta);
        }
        
        public void SetCurrentState(State state)
        {
            _currentState?.Exit();
            _currentState = state;
            _currentState?.Enter();
        }

        public void AddState(uint key, State state)
        {
            _states.Add(key, state);
        }

        public State GetState(uint key)
        {
            return _states.TryGetValue(key, out var state) ? state : null;
        }
    }
}
