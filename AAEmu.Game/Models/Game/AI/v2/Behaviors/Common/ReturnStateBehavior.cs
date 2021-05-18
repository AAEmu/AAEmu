using System;
using AAEmu.Game.Core.Packets.G2C;
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

            Ai.Owner.ClearAllAggro();
            Ai.Owner.SetTarget(null);
            // TODO: Ai.Owner.DisableAggro();
            
            var needRestorationOnReturn = true; // TODO: Use params & alertness values
            if (needRestorationOnReturn)
            {
                // StartSkill RETURN SKILL TYPE
                Ai.Owner.Buffs.AddBuff((uint)BuffConstants.NPC_RETURN_BUFF, Ai.Owner);
                Ai.Owner.Hp = Ai.Owner.MaxHp;
                Ai.Owner.Mp = Ai.Owner.MaxMp;
                Ai.Owner.BroadcastPacket(new SCUnitPointsPacket(Ai.Owner.ObjId, Ai.Owner.Hp, Ai.Owner.Mp), true);
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
            
            _timeoutTime = DateTime.Now.AddSeconds(20); 
        }

        public override void Tick(TimeSpan delta)
        {
            Ai.Owner.MoveTowards(Ai.IdlePosition, 2.4f * (delta.Milliseconds / 1000.0f)); // TODO: Get proper npc speed
            
            var distanceToIdle = MathUtil.CalculateDistance(Ai.IdlePosition, Ai.Owner.Position);
            if (distanceToIdle < 1.0f)
                OnCompletedReturnNoTeleport();
            
            if (DateTime.Now > _timeoutTime)
                OnCompletedReturn();
        }

        private void OnCompletedReturn()
        {
            var distanceToIdle = MathUtil.CalculateDistance(Ai.IdlePosition, Ai.Owner.Position);
            if (distanceToIdle > 2 * 2)
            {
                Ai.Owner.MoveTowards(Ai.IdlePosition, 1000000.0f);
                Ai.Owner.StopMovement();
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
