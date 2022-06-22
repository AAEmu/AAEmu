using System;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class MoveToTargetState : State
    {
        public Unit Target { get; set; }
        private float PreviousDistance = 0.0f;

        public override void Enter()
        {
            _log.Debug("Entering MoveToTargetState - {0}", Target.Name);
            PreviousDistance = MathUtil.CalculateDistance(AI.Owner, Target, true);
        }

        public override void Tick(TimeSpan delta)
        {
            if (!(AI.Owner is Npc npc))
                return;

            // if (PreviousDistance > AI.Params.CombatRange)
            if (PreviousDistance > 2.5f)
            {
                npc.MoveTowards(Target.Transform.World.Position, 3.4f * (delta.Milliseconds / 1000.0f));
            }
            else
            {
                // Send NPC stop movement packet
                npc.StopMovement();
                // Go in combat 
            }
            
            PreviousDistance = MathUtil.CalculateDistance(AI.Owner, Target, true);
        }
    }
}
