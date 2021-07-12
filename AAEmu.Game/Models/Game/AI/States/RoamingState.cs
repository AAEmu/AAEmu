using System;
using System.Numerics;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class RoamingState : State
    {
        private Vector3 _targetLoc = new Vector3();
        private Npc _owner;

        public override void Enter()
        {
            if (!(AI.Owner is Npc npc))
                return;
            // _targetLoc = AIUtils.CalcNextRoamingPosition(AI);
            _owner = npc;
        }

        public override void Tick(TimeSpan delta)
        {
            _owner.MoveTowards(_targetLoc, 4.4f * (delta.Milliseconds / 1000.0f));
            if (MathUtil.CalculateDistance(_owner.Transform.World.Position, _targetLoc, true) < 1.0f)
            {
                _owner.StopMovement();
                GoToIdle();
            }
        }
        
        private void GoToIdle()
        {
            var idleState = AI.StateMachine.GetState(Framework.States.Idle);
            AI.StateMachine.SetCurrentState(idleState);
        }
    }
}
