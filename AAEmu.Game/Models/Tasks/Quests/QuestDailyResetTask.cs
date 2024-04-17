using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Quests;

public class QuestDailyResetTask : Task
{
    /// <summary>
    /// Task used to do the quest resets for daily quests
    /// </summary>
    public QuestDailyResetTask()
    {

    }

    public override void Execute()
    {
        foreach (var character in WorldManager.Instance.GetAllCharacters())
        {
            character.Quests.ResetDailyQuests(true);
        }
    }
}
