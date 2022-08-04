using System.Collections.Generic;
using System.Linq;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Templates
{
    public class QuestTemplate : IQuestTemplate
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
        public uint DetailId { get; set; }
        public uint ZoneId { get; set; }
        public int Degree { get; set; }
        public bool UseQuestCamera { get; set; }
        public int Score { get; set; }
        public bool UseAcceptMessage { get; set; }
        public bool UseCompleteMessage { get; set; }
        public uint GradeId { get; set; }
        public IDictionary<uint, QuestComponent> Components { get; set; }

        public QuestTemplate()
        {
            Components = new Dictionary<uint, QuestComponent>();
        }

        public QuestComponent GetFirstComponent(QuestComponentKind step)
        {
            return Components.Values
                    .FirstOrDefault(cp => cp.KindId == step);
        }
        public QuestComponent[] GetComponents(QuestComponentKind step)
        {
            return Components.Values
                    .Where(cp => cp.KindId == step)
                    .ToArray();
        }
    }
}
