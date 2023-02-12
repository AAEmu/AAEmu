namespace AAEmu.Game.Models.Tasks.Duels
{
    public abstract class DuelFuncTask : Task
    {
        protected uint _challengerId;
        protected uint _challengedId;
        
        protected DuelFuncTask(uint challengerId, uint challengedId)
        {
            _challengerId = challengerId;
            _challengedId = challengedId;
        }
    }
}
