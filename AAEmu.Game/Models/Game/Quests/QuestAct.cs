using System;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests
{
    public class QuestAct : IComparable<QuestAct>, IQuestAct
    {
        public uint Id { get; set; }
        public uint ComponentId { get; set; }
        public uint DetailId { get; set; }
        public string DetailType { get; set; }
        public QuestActTemplate Template { get; set; }
    
        public QuestActTemplate GetTemplate()
        {
            return QuestManager.Instance.GetActTemplate(DetailId, DetailType);
        }

        public T GetTemplate<T>() where T : QuestActTemplate
        {
            return QuestManager.Instance.GetActTemplate<T>(DetailId, DetailType);
        }

        public bool Use(ICharacter character, Quest quest, int objective)
        {
            var template = QuestManager.Instance.GetActTemplate(DetailId, DetailType);
            return template.Use(character, quest, objective);
        }

        /*
         * To sort an array
         */
        public int CompareTo(QuestAct other)
        {
            return Id.CompareTo(other.Id);
        }
    }
}
