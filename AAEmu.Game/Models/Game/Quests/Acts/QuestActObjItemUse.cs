﻿using System.Security.AccessControl;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjItemUse : QuestActTemplate
{
    public uint ItemId { get; set; }
    public int Count { get; set; }
    public uint HighlightDoodadId { get; set; }
    public int HighlightDoodadPhase { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }
    public bool DropWhenDestroy { get; set; }

    public static int ItemUseStatus { get; private set; } = 0;

    private int Objective { get; set; }

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Warn("QuestActObjItemUse");
        if (quest.Template.Score > 0) // Check if the quest use Template.Score or Count
        {
            ItemUseStatus = objective * Count; // Count в данном случае % за единицу
            quest.OverCompletionPercent = ItemUseStatus + QuestActEtcItemObtain.ItemObtainStatus + QuestActObjMonsterHunt.HuntStatus + QuestActObjMonsterGroupHunt.GroupHuntStatus + QuestActObjInteraction.InteractionStatus;

            if (quest.Template.LetItDone)
            {
                if (quest.OverCompletionPercent >= quest.Template.Score * 3 / 5)
                    quest.EarlyCompletion = true;

                if (quest.OverCompletionPercent > quest.Template.Score)
                    quest.ExtraCompletion = true;
            }

            Update();

            return quest.OverCompletionPercent >= quest.Template.Score;
        }

        if (quest.Template.LetItDone)
        {
            quest.OverCompletionPercent = objective * 100 / Count;

            if (quest.OverCompletionPercent >= 60)
                quest.EarlyCompletion = true;

            if (quest.OverCompletionPercent > 100)
                quest.ExtraCompletion = true;
        }

        Update();

        return objective >= Count;
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
        _log.Info("Получим, сколько уже имеем предметов по заданию.");

        return Objective;
    }
}
