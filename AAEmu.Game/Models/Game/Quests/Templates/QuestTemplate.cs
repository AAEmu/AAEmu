using System.Collections.Generic;
using System.Linq;

using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Templates
{
    public class QuestTemplate
    {
        public uint Id { get; set; }
        public bool Repeatable { get; set; }
        public byte Level { get; set; }
        public bool Selective { get; set; }
        public bool Successive { get; set; }
        public bool RestartOnFail { get; set; }
        public uint ChapterIdx { get; set; }
        public uint QuestIdx { get; set; }
        public uint MilestoneId { get; set; }
        public bool LetItDone { get; set; }
        public QuestDetail DetailId { get; set; }
        public uint ZoneId { get; set; }
        public int Degree { get; set; }
        public bool UseQuestCamera { get; set; }
        public int Score { get; set; }
        public bool UseAcceptMessage { get; set; }
        public bool UseCompleteMessage { get; set; }
        public uint GradeId { get; set; }
        public Dictionary<uint, QuestComponent> Components { get; set; }

        public QuestTemplate()
        {
            Components = new Dictionary<uint, QuestComponent>();
        }

        public QuestComponent GetComponent(QuestComponentKind step)
        {
            foreach (var component in Components.Values)
                if (component.KindId == step)
                    return component;
            return null;
        }
        public List<QuestComponent> GetComponents(QuestComponentKind step)
        {
            return Components.Values.Where(c => c.KindId == step).ToList();
        }
    }
}
