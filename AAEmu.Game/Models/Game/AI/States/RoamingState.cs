using System;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class RoamingState : State
    {
        private Point _targetLoc;

        public override void Enter()
        {
            _targetLoc = AIUtils.CalcNextRoamingPosition(AI);
        }

        public override void Tick(TimeSpan delta)
        {
            // Rotate to face point
            // Get AI move speed
            // Move by speed towards point 
        }
    }
}
