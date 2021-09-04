using System;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.AI.Framework;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class IdleState : State
    {
        private float _maxIdleTime;
        private TimeSpan _idleTimeSpan;
        private DateTime _stateStart;

        /// <summary>
        /// This state is used when waiting for other actions to trigger new states
        /// Passing a maxIdleTime will pick between 0 and maxIdleTime to go to NextState, from AI.GetNextState()
        /// </summary>
        /// <param name="maxIdleTime"></param>
        public IdleState(float maxIdleTime = 0.0f)
        {
            _maxIdleTime = maxIdleTime;
        }
        
        public override void Enter()
        {
            // Pick time to Idle
            if (_maxIdleTime > 0)
            {
                _stateStart = DateTime.UtcNow;
                _idleTimeSpan = TimeSpan.FromSeconds(Rand.Next(0, _maxIdleTime));
            }
        }

        public override void Tick(TimeSpan delta)
        {
            if (_maxIdleTime > 0 && _stateStart + _idleTimeSpan > DateTime.UtcNow)
            {
                var stateId = AI.GetNextState(this);
                var state = AI.StateMachine.GetState(stateId);
                AI.StateMachine.SetCurrentState(state);
            }
        }
    }
}
