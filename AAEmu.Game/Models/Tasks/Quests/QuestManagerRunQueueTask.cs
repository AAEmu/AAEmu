using AAEmu.Game.Core.Managers;

namespace AAEmu.Game.Models.Tasks.Quests;

public class QuestManagerRunQueueTask : Task
{
    public QuestManagerRunQueueTask()
    {
        //
    }

    public override void Execute()
    {
        QuestManager.Instance.DoQueuedEvaluations();
    }
}
