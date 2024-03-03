using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActConAcceptNpcEmotion : QuestActTemplate
{
    public uint NpcId { get; set; }
    public string Emotion { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActConAcceptNpcEmotion: NpcId {NpcId}, Emotion {Emotion}");

        if (character.CurrentTarget is not Npc npc)
            return false;

        quest.QuestAcceptorType = QuestAcceptorType.Npc;
        quest.AcceptorType = NpcId;

        return npc.TemplateId == NpcId;
    }
}
