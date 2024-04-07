﻿using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Models.Game.Quests.Acts;

public class QuestActCheckSphere(QuestComponentTemplate parentComponent) : QuestActTemplate(parentComponent)
{
    public uint SphereId { get; set; }

    public override bool Use(ICharacter character, Quest quest, IQuestAct questAct, int objective)
    {
        Logger.Debug($"QuestActCheckSphere: SphereId {SphereId}");
        return false;
    }

    public override void InitializeAction(Quest quest, IQuestAct questAct)
    {
        base.InitializeAction(quest, questAct);
        SphereQuestManager.Instance.AddSphereQuestTriggers(quest.Owner, quest, parentComponent.Id, 0);
        quest.Owner.Events.OnEnterSphere += OnEnterSphere;
        quest.Owner.Events.OnExitSphere += OnExitSphere;
    }

    public override void FinalizeAction(Quest quest, IQuestAct questAct)
    {
        SphereQuestManager.Instance.RemoveSphereQuestTriggers(quest.Owner.Id, (uint)quest.Id);
        quest.Owner.Events.OnEnterSphere -= OnEnterSphere;
        quest.Owner.Events.OnExitSphere -= OnExitSphere;
        base.FinalizeAction(quest, questAct);
    }

    /// <summary>
    /// Checks if you are inside a specific Quest Sphere
    /// </summary>
    /// <param name="quest"></param>
    /// <param name="questAct"></param>
    /// <param name="currentObjectiveCount"></param>
    /// <returns></returns>
    public override bool RunAct(Quest quest, IQuestAct questAct, int currentObjectiveCount)
    {
        Logger.Debug($"QuestActCheckSphere({DetailId}).RunAct: Quest {quest.TemplateId}, SphereId {SphereId}");
        return currentObjectiveCount > 0;
    }

    private void OnEnterSphere(object sender, OnEnterSphereArgs e)
    {
        if (e.SphereQuest.QuestId != ParentQuestTemplate.Id)
            return;
        SetObjective(e.OwningQuest, 1);
    }

    private void OnExitSphere(object sender, OnExitSphereArgs e)
    {
        if (e.SphereQuest.QuestId != ParentQuestTemplate.Id)
            return;
        SetObjective(e.OwningQuest, 0);
    }
}
