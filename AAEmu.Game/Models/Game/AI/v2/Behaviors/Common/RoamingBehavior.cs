using System;
using AAEmu.Game.Models.Game.AI.Utils;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class RoamingBehavior : Behavior
    {
        private Point _targetRoamPosition;
        private DateTime _nextRoaming;
        
        public override void Enter()
        {
            UpdateRoaming();
        }

        public override void Tick(TimeSpan delta)
        {
            if (_targetRoamPosition == null && DateTime.UtcNow > _nextRoaming)
                UpdateRoaming();
            
            if (_targetRoamPosition == null)
                return;
            
            Ai.Owner.MoveTowards(_targetRoamPosition, 1.8f * (delta.Milliseconds / 1000.0f), 5);
            var dist = MathUtil.CalculateDistance(Ai.Owner.Position, _targetRoamPosition);
            if (dist < 1.0f)
            {
                Ai.Owner.StopMovement();
                _targetRoamPosition = null;
                _nextRoaming = DateTime.UtcNow.AddSeconds(3); // Rand 3-6 would look nice ?
            }
        }

        public override void Exit()
        {
        }

        private void UpdateRoaming()
        {
            // TODO : Group member handling
            
            _targetRoamPosition = AIUtils.CalcNextRoamingPosition(Ai);
        }
    }
}
