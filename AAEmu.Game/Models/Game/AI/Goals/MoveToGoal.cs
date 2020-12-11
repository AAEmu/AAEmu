using AAEmu.Game.Models.Game.AI.Framework;

namespace AAEmu.Game.Models.Game.AI.Goals
{
    public class MoveToGoal : AbstractGoal
    {
        public override bool CanRun()
        {
            return AI.TargetPosition != null;
        }

        public override void Execute()
        {
            // Get unit movement speed
            // Get distance to move during tick
            // Move to new position, send packet to client for AbstractUnitAI
            // If new position == TargetPosition
            //     TargetPosition = null;
        }
    }
}
