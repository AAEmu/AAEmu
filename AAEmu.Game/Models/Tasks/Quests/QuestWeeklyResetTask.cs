using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models;
using NLog;

namespace AAEmu.Game.Models.Tasks.Quests;

/// <summary>
/// Monday 00:00:00 UTC (cron <c>0 0 0 * * 1</c>, NCrontab Sunday=0).
/// Clears completed_quests bits for weekly detail quests for all online characters.
/// Offline players catch up via leave_time week-boundary on login.
/// </summary>
public class QuestWeeklyResetTask : Task
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Execute()
    {
        Logger.Info(
            "Server weekly reset (UTC {0:yyyy-MM-dd HH:mm:ss}Z, week start {1:yyyy-MM-dd})",
            ServerCalendar.UtcNow,
            ServerCalendar.WeekStartMondayUtc);

        foreach (var character in WorldManager.Instance.GetAllCharacters())
            character.Quests.ResetWeeklyQuests(true);
    }
}
