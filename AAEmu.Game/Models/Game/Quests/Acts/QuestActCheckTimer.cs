using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Char;

namespace AAEmu.Game.Models.Game.Quests.Acts
{
    public class QuestActCheckTimer : QuestActTemplate
    {
        public int LimitTime { get; set; }
        public bool ForceChangeComponent { get; set; }
        public uint NextComponent { get; set; }
        public bool PlaySkill { get; set; }
        public uint SkillId { get; set; }
        public bool CheckBuff { get; set; }
        public uint BuffId { get; set; }
        public bool SustainBuff { get; set; }
        public uint TimerNpcId { get; set; }
        public bool IsSkillPlayer { get; set; }

        public override bool Use(Character character, Quest quest, int objective)
        {
            _log.Warn("QuestActCheckTimer");
            return false;
        }
    }
}
