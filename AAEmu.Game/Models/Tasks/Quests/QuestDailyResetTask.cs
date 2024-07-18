using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;

namespace AAEmu.Game.Models.Tasks.Quests;

/// <summary>
/// Task that triggers daily, used for resetting daily quests, and updating daily login when the player is online
/// </summary>
public class QuestDailyResetTask : Task
{
    /// <summary>
    /// Task used to do the quest resets for daily quests
    /// </summary>
    public QuestDailyResetTask()
    {

    }

    public override System.Threading.Tasks.Task ExecuteAsync()
    {
        foreach (var character in WorldManager.Instance.GetAllCharacters())
        {
            character.Quests.ResetDailyQuests(true);
            TimedRewardsManager.Instance.DoDailyAccountLogin(character.AccountId);
        }

        return System.Threading.Tasks.Task.CompletedTask;
    }
}
