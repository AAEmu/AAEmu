using System;
using System.Collections.Generic;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Templates;
using AAEmu.Game.Models.Game.Skills;
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
        Logger.Warn("QuestActCheckTimer");
        // TODO add what to do with timer
        // TODO настройка и старт таймера ограничения времени на квест
        var task = new Dictionary<uint, QuestTimeoutTask>
        {
            { quest.TemplateId, new QuestTimeoutTask(character, quest.TemplateId) }
        };

        if (!QuestManager.Instance.QuestTimeoutTask.ContainsKey(quest.Owner.Id))
        {
            QuestManager.Instance.QuestTimeoutTask.Add(quest.Owner.Id, task);
        }
        else
        {
            if (!QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].ContainsKey(quest.TemplateId))
                QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id].Add(quest.TemplateId, new QuestTimeoutTask(character, quest.TemplateId));
            else
                QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id][quest.TemplateId] = new QuestTimeoutTask(character, quest.TemplateId);
        }


        TaskManager.Instance.Schedule(QuestManager.Instance.QuestTimeoutTask[quest.Owner.Id][quest.TemplateId], TimeSpan.FromMilliseconds(LimitTime));
        character.SendMessage("[Quest] {0}, quest {1} will end in {2} minutes.", character.Name, quest.TemplateId, LimitTime / 60000);
        quest.Time = DateTime.UtcNow.AddMilliseconds(LimitTime);

        //if (SustainBuff)
        //{
        //    // BUFF: Overburdened - Carrying heavy objects reduces movement speed and prevents teleporting or gliding.
        //    var buffId = (uint)BuffConstants.Overburdened;
        //    character.Buffs.AddBuff(new Buff(character, character, SkillCaster.GetByType(SkillCasterType.Unit), SkillManager.Instance.GetBuffTemplate(buffId), null, DateTime.UtcNow));
        //}

        return true;
    }
}
