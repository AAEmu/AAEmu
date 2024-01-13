using System;
using System.Security.AccessControl;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjItemGroupGather : QuestActTemplate
{
    public uint ItemGroupId { get; set; }
    public int Count { get; set; }
    public bool Cleanup { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }
    public bool DestroyWhenDrop { get; set; }

    //public static int GroupGatherStatus { get; private set; } = 0;
    private int Objective { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActObjItemGroupGather: ItemGroupId {0}, Count {1}, UseAlias {2}, QuestActObjAliasId {3}, HighlightDoodadId {4}, HighlightDoodadPhase {5}, quest {6}, objective {7}, Score {8}",
            ItemGroupId, Count, UseAlias, QuestActObjAliasId, HighlightDoodadId, HighlightDoodadPhase, quest.TemplateId, objective, quest.Template.Score);

        var res = false;
        var maxCleanup = quest.Template.LetItDone ? Count * 7 / 5 : Count;
        if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
        {
            quest.GroupGatherStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = quest.GroupGatherStatus + quest.HuntStatus + quest.GroupHuntStatus + quest.InteractionStatus;

            if (quest.Template.LetItDone)
            {
                if (quest.OverCompletionPercent >= quest.Template.Score * 1 / 2)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > quest.Template.Score)
                    quest.ExtraCompletion = true;
            }

            Update();

            res = quest.OverCompletionPercent >= quest.Template.Score;
        }
        else
        {
            if (quest.Template.LetItDone)
            {
                quest.OverCompletionPercent = objective * 100 / Count;

                if (quest.OverCompletionPercent >= 50)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > 100)
                    quest.ExtraCompletion = true;
            }

            Update();

            res = objective >= Count;
        }

        if (res && Cleanup)
            quest.QuestCleanupItemsPool.Add(new ItemCreationDefinition(ItemGroupId, Math.Min(maxCleanup, objective)));

        return res;
    }
    public override void Update()
    {
        Objective++;
    }
    public override bool IsCompleted()
    {
        return Objective >= Count;
    }
    public override int GetCount()
    {
        Logger.Info("Получим, информацию на сколько выполнено задание.");

        return Objective;
    }
    public override void ClearStatus()
    {
        //GroupGatherStatus = 0;
        Objective = 0;
        Logger.Info("Сбросили статус в ноль.");
    }
}
