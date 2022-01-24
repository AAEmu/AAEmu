using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.Quests
{
    public class QuestTimeoutTask : Task
    {
        private Character _owner;
        private uint _questId;

        public QuestTimeoutTask(Character owner, uint questId)
        {
            _owner = owner;
            _questId = questId;
        }

        public override void Execute()
        {
            QuestManager.Instance.CancelQuest(_owner, _questId);
        }
    }
}
