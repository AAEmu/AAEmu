using AAEmu.Game.Models.Game.Char;
using NLog;

namespace AAEmu.Game.Models.Game.Quests.Templates
{
    public abstract class QuestActTemplate
    {
        public static Logger _log = LogManager.GetCurrentClassLogger();
        public uint Id { get; set; }

        public abstract bool Use(ICharacter character, Quest quest, int objective);
    }
}
