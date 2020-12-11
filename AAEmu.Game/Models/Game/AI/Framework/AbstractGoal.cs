namespace AAEmu.Game.Models.Game.AI.Framework
{
    public abstract class AbstractGoal
    {
        public AbstractAI AI;

        /*
         * How do we want to handle goals ?
         * - On each AI tick, look at which goals can run
         * - Run the goal with highest priority
         */
        
        
        public abstract bool CanRun();
        public abstract void Execute();
    }
}
