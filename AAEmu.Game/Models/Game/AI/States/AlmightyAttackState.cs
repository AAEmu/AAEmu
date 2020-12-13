using System;
using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.NPChar;

namespace AAEmu.Game.Models.Game.AI.States
{
    public class AlmightyAttackState : State
    {
        public override void Enter()
        {
            if (!(AI.Owner is Npc npc))
                _log.Error("State applied to invalid unit type");
            
            // TODO : Save ref to ai params
        }

        public override void Tick(TimeSpan delta)
        {
            // Check distance to aggro list, top to bottom
                // If no one is within return distance, reset HP, MP and go back to idle position

            // Add to delay timer
            
            // Get next skill
            
            // Cast next skill
        }
    }
}
