using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Quests;

public class QuestManagerRunQueueTask : Task
{
    public QuestManagerRunQueueTask()
    {
        //
    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        QuestManager.Instance.DoQueuedEvaluations();

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
