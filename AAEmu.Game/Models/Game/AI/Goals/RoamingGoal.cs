using AAEmu.Game.Models.Game.AI.Framework;
using AAEmu.Game.Models.Game.AI.Utils;

namespace AAEmu.Game.Models.Game.AI.Goals
{
    public class RoamingGoal : AbstractGoal
    {
        public override bool CanRun()
        {
            // If our target location is null, we want to find a new one
            return AI.TargetPosition == null;
        }

        public override void Execute()
        {
            AI.TargetPosition = AIUtils.CalcNextRoamingPosition(AI);
        }
    }
}
