using System.Collections.Generic;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Core.Managers;

public interface IQuestManager
{
    void FailQuest(ICharacter owner, uint questId);
    bool CheckGroupItem(uint groupId, uint itemId);
    bool CheckGroupNpc(uint groupId, uint npcId);
    List<QuestActTemplate> GetActs(uint id);
    QuestActTemplate GetActTemplate(uint id, string type);
    T GetActTemplate<T>(uint id, string type) where T : QuestActTemplate;
    List<uint> GetGroupItems(uint groupId);
    QuestSupplies GetSupplies(byte level);
    QuestTemplate GetTemplate(uint id);
    void Load();
    void EnqueueEvaluation(Quest quest);
}
