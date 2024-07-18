using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Tasks.Quests;

public class QuestTimeoutTask : Task
{
    private readonly ICharacter _owner;
    private readonly uint _questId;

    /// <summary>
    /// Task that triggers a OnTimerExpired event upon execution
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="questId"></param>
    public QuestTimeoutTask(ICharacter owner, uint questId)
    {
        _owner = owner;
        _questId = questId;
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        QuestManager.Instance.OnTimerExpired(_owner, _questId);

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
