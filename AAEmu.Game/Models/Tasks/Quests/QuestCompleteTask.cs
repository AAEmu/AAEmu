using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Tasks.Quests
{
    public class QuestCompleteTask : Task
    {
        private ICharacter _owner;
        private uint _questId;

        public QuestCompleteTask(ICharacter owner, uint questId)
        {
            _owner = owner;
            _questId = questId;
        }

        public override void Execute()
        {
            QuestManager.Instance.QuestCompleteTask(_owner, _questId);
        }
    }
}
