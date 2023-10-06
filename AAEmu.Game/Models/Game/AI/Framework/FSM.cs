using System;
using System.Collections.Generic;

namespace AAEmu.Game.Models.Game.AI.Framework;

public class FSM
{
    protected Dictionary<States, State> _states = new();
    protected State _currentState;

    public FSM() { }

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
