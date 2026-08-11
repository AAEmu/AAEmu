using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.Models.Tasks.Quests;

/// <summary>
/// Fires at 00:00:00 UTC every day (cron <c>0 0 0 */1 * *</c> on TaskManager's UTC clock).
/// Resets all daily-detail completed quests (not just Path of Destiny), today-assignment state,
/// and daily-login account rewards for online characters. Offline catch-up is leave_time based.
/// </summary>
public class QuestDailyResetTask : Task
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Execute()
    {
        Logger.Info(
            "Server daily reset (UTC {0:yyyy-MM-dd HH:mm:ss}Z) — daily/hunt/group/livelihood/today completed_quests",
            ServerCalendar.UtcNow);

        foreach (var character in WorldManager.Instance.GetAllCharacters())
        {
            character.Quests.ResetDailyQuests(true);
            TimedRewardsManager.Instance.DoDailyAccountLogin(character.AccountId);
        }

        // Today (detail 13) completion bits above; also reseed Path of Destiny UI/DB day_key state.
        TodayAssignmentManager.Instance.OnServerDailyReset();
    }
}
