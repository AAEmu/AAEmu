using System;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.v2.Behaviors
{
    public class ReturnStateBehavior : Behavior
    {
        private DateTime _timeoutTime;
        
        public override void Enter()
        {
            // TODO : Autodisable
            
            Ai.Owner.ClearAggro();
            // TODO: Ai.Owner.DisableAggro();
            
            var needRestorationOnReturn = true; // TODO: Use params & alertness values
            if (needRestorationOnReturn)
            {
                // StartSkill RETURN SKILL TYPE
                Ai.Owner.Buffs.AddBuff((uint)BuffConstants.NPC_RETURN_BUFF, Ai.Owner);
            }

            var alwaysTeleportOnReturn = false; // get from params
            if (alwaysTeleportOnReturn)
            {
                OnCompletedReturn();
            }

            var goReturnState = true; // get from params
            if (!goReturnState)
            {
                OnCompletedReturnNoTeleport();
            }
            
            _timeoutTime = DateTime.UtcNow.AddSeconds(20); 
        }

        public override void Tick(TimeSpan delta)
        {
            Ai.Owner.MoveTowards(Ai.IdlePosition, (float) (2.4 * (delta.Milliseconds / 1000.0f))); // TODO: Get proper npc speed
            
            var distanceToIdle = MathUtil.CalculateDistance(Ai.IdlePosition, Ai.Owner.Position);
            if (distanceToIdle < 1.0f)
                OnCompletedReturnNoTeleport();
            
            if (DateTime.UtcNow > _timeoutTime)
                OnCompletedReturn();
        }

        private void OnCompletedReturn()
        {
            var distanceToIdle = MathUtil.CalculateDistance(Ai.IdlePosition, Ai.Owner.Position);
            if (distanceToIdle > 2 * 2)
            {
                // teleport to idle
            }

            OnCompletedReturnNoTeleport();
        }

        public void OnCompletedReturnNoTeleport()
        {
            // TODO: Handle return signal override
            Ai.GoToRunCommandSet();
        }

        public override void Exit()
        {
            // TODO: Ai.Owner.EnableAggro();
            
            Ai.Owner.Buffs.RemoveBuff((uint)BuffConstants.NPC_RETURN_BUFF);
        }
    }
}
