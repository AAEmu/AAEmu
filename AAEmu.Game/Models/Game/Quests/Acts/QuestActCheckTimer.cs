using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Tasks.Quests;

namespace AAEmu.Game.Models.Game.Quests.Acts;

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

    public override bool Use(ICharacter character, Quest quest, int objective)
    {
        Logger.Debug("QuestActCheckTimer");
        // TODO: Add what to do with timer

        // Setting up and starting the time limit timer for the quest
        // настройка и старт таймера ограничения времени на квест
        var task = new Dictionary<uint, QuestTimeoutTask>
        {
            { ParentQuestTemplate.Id, new QuestTimeoutTask(character, quest.TemplateId) }
        };

        if (!QuestManager.Instance.QuestTimeoutTask.ContainsKey(quest.Owner.Id))
        {
            QuestManager.Instance.QuestTimeoutTask.Add(quest.Owner.Id, task);
        }
        else
        {
            if (!QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].ContainsKey(ParentQuestTemplate.Id))
                QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].Add(ParentQuestTemplate.Id, new QuestTimeoutTask(character, ParentQuestTemplate.Id));
            else
                QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id][ParentQuestTemplate.Id] = new QuestTimeoutTask(character, ParentQuestTemplate.Id);
        }


        TaskManager.Instance.Schedule(QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id][ParentQuestTemplate.Id], TimeSpan.FromMilliseconds(LimitTime));
        character.SendMessage($"[Quest] {character.Name}, quest {ParentQuestTemplate.Id} will end in {LimitTime / 60000} minutes.");
        quest.Time = DateTime.UtcNow.AddMilliseconds(LimitTime);

        return true;
    }
}
