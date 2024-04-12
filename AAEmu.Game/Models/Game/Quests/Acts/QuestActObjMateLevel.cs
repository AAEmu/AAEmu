using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Items.Templates;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActObjMateLevel(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint ItemId { get; set; }
    public byte Level { get; set; }
    public bool Cleanup { get; set; }
    public bool UseAlias { get; set; }
    public uint QuestActObjAliasId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug("QuestActObjMateLevel");
        return character.Mates.GetMateInfo(ItemId).Level >= Level;
    }

    /// <summary>
    /// Checks if any of your items is the required summon item, and if it's level has been met
    /// </summary>
    /// <param name="quest"></param>
    /// <returns>Level of the first mate that was valid</returns>
    private byte CalculateObjective(Quest quest)
    {
        if (!quest.Owner.Inventory.GetAllItemsByTemplate([], ItemId, -1, out var validItems, out _))
        {
            SetObjective(quest, 0);
            return 0;
        }

        foreach (var item in validItems)
        {
            if (item is not SummonMate summonMate)
                continue;

            var res = summonMate.DetailLevel >= Level;
            if (res)
            {
                SetObjective(quest, 1);
                return summonMate.DetailLevel;
            }
        }

        SetObjective(quest, 0);
        return 0;
    }

    /// <summary>
    /// Checks if you own a mate of specified type that is at least given Level
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        var res = CalculateObjective(quest);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).RunAct: Quest: {quest.TemplateId}, Owner {quest.Owner.Name} ({quest.Owner.Id}), Level {res}/{Level}");
        return res > 0;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        quest.Owner.Events.OnMateLevelUp += questAct.OnMateLevelUp;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        quest.Owner.Events.OnMateLevelUp -= questAct.OnMateLevelUp;
        base.FinalizeAction(quest, questAct);
    }

    /// <summary>
    /// Mate LevelUp event
    /// </summary>
    /// <param name="questAct"></param>
    /// <param name="sender">Mate</param>
    /// <param name="args"></param>
    public override void OnMateLevelUp(IQuestAct questAct, object sender, OnMateLevelUpArgs args)
    {
        if (questAct.Id != ActId)
            return;

        var res = CalculateObjective(questAct.QuestComponent.Parent.Parent);
        Logger.Debug($"{QuestActTemplateName}({DetailId}).OnMateLevelUp: Quest: {questAct.QuestComponent.Parent.Parent.TemplateId}, Owner {questAct.QuestComponent.Parent.Parent.Owner.Name} ({questAct.QuestComponent.Parent.Parent.Owner.Id}), Level {res}/{Level}");
    }
}
