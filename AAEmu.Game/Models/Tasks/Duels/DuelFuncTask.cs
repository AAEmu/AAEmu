namespace AAEmu.Game.Models.Tasks.Duels;

public abstract class DuelFuncTask(uint challengerId, uint challengedId) : Task
{
    protected uint _challengerId = challengerId;
    protected uint _challengedId = challengedId;
}
