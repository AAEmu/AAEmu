using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Tasks.Quests
{
    public class QuestDailyResetTask : Task
    {
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
}
