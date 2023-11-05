using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjItemGather : QuestActTemplate // Сбор предметов
{
    public uint ItemId { get; set; }
    public int Count { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool Cleanup { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    public static int GatherStatus { get; private set; } = 0;

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActObjItemGather: QuestActObjItemGatherId {0}, Count {1}, UseAlias {2}, QuestActObjAliasId {3}, HighlightDoodadId {4}, HighlightDoodadPhase {5}, quest {6}, objective {7}, Score {8}",
            ItemId, Count, UseAlias, QuestActObjAliasId, HighlightDoodadId, HighlightDoodadPhase, quest.TemplateId, objective, quest.Template.Score);

        var res = false;
        var maxCleanup = quest.Template.LetItDone ? Count * 7 / 5 : Count;
        if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
        {
            GatherStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = GatherStatus + QuestActObjMonsterHunt.HuntStatus + QuestActObjMonsterGroupHunt.GroupHuntStatus + QuestActObjInteraction.InteractionStatus;

            if (quest.Template.LetItDone)
            {
                if (quest.OverCompletionPercent >= quest.Template.Score * 3 / 5)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > quest.Template.Score)
                    quest.ExtraCompletion = true;
            }

            res = quest.OverCompletionPercent >= quest.Template.Score;
        }
        else
        {
            if (quest.Template.LetItDone)
            {
                quest.OverCompletionPercent = objective * 100 / Count;

                if (quest.OverCompletionPercent >= 60)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > 100)
                    quest.ExtraCompletion = true;
            }
            res = objective >= Count;
        }

        if (res && Cleanup)
            quest.QuestCleanupItemsPool.Add(new ItemCreationDefinition(ItemId, Math.Min(maxCleanup, objective)));

        return res;
    }
}
