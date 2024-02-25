using System.Linq;

using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Utils;

public class QuestCommandUtil
{
    public static void GetCommandChoice(ICharacter character, string choice, string[] args)
    {
        uint questId;

        switch (choice)
        {
            case "add":
                if (args.Length >= 2)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        character.Quests.Add(questId, true);
                    }
                }
                else
                {
                    character.SendMessage("[Quest] Proper usage: /quest add <questId>\nBefore that, target the Npc you need for the quest");
                }
                break;
            case "list":
                character.SendMessage("[Quest] LIST");
                foreach (var quest in character.Quests.ActiveQuests.Values)
                {
                    var objectives = quest.GetObjectives(quest.Step).Select(t => t.ToString()).ToList();
                    character.SendMessage($"Quest {quest.Template.Id}: Step({quest.Step}), Objectives({string.Join(", ", objectives)})");
                }
                break;
            case "reward":
                if (args.Length >= 2)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        if (args.Length >= 3 && int.TryParse(args[2], out var selectedId))
                        {
                            character.Quests.Complete(questId, selectedId);
                        }
                        else
                        {
                            character.Quests.Complete(questId, 0);
                        }
                    }
                }
                else
                {
                    character.SendMessage("[Quest] Proper usage: /quest reward <questId>");
                }
                break;
            case "step":
                if (args.Length >= 2)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        if (character.Quests.HasQuest(questId))
                        {
                            if (args.Length >= 3 && uint.TryParse(args[2], out var stepId))
                            {
                                if (character.Quests.SetStep(questId, stepId))
                                    character.SendMessage($"[Quest] set Step {stepId} for Quest {questId}");
                                else
                                    character.SendMessage("[Quest] Proper usage: /quest step <questId> <stepId>");
                            }
                        }
                        else
                        {
                            character.SendMessage($"[Quest] You do not have the quest {questId}");
                        }
                    }
                }
                else
                {
                    character.SendMessage("[Quest] Proper usage: /quest step <questId> <stepId>");
                }
                break;
            case "prog":
                if (args.Length >= 2)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        if (character.Quests.HasQuest(questId))
                        {
                            var quest = character.Quests.ActiveQuests[questId];
                            if (quest.Step == QuestComponentKind.None)
                                quest.Step = QuestComponentKind.Start;
                            if (quest.Step == QuestComponentKind.Start)
                                quest.Step = QuestComponentKind.Supply;
                            else if (quest.Step == QuestComponentKind.Supply)
                                quest.Step = QuestComponentKind.Progress;
                            else if (quest.Step == QuestComponentKind.Progress)
                                quest.Step = QuestComponentKind.Ready;
                            else if (quest.Step == QuestComponentKind.Ready)
                                quest.Step = QuestComponentKind.Reward;
                            else if (quest.Step > QuestComponentKind.Reward)
                            {
                                quest.Drop(true);
                                break;
                            }
                            character.SendMessage($"[Quest] Perform step {quest.Step} for quest {questId}");
                            quest.Update();
                        }
                        else
                        {
                            character.SendMessage($"[Quest] You do not have the quest {questId}");
                        }
                    }
                }
                else
                {
                    character.SendMessage("[Quest] Proper usage: /quest update <questId>");
                }
                break;
            case "remove":
                if (args.Length >= 2)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        if (character.Quests.HasQuest(questId))
                        {
                            character.Quests.Drop(questId, true, true); // удаляем и из CompletedQuests
                        }
                        else
                        {
                            character.SendMessage($"[Quest] You do not have the quest {questId}");
                        }
                        // посылаем пакеты для того, что-бы клиент был в курсе обновления квестов
                        character.Quests.Send();
                        character.Quests.SendCompleted();
                        character.Quests.RecallEvents();
                    }
                }
                else
                {
                    character.SendMessage("[Quest] Proper usage: /quest remove <questId>");
                }
                break;
            case "resetdaily":
                character.Quests.ResetDailyQuests(true);
                break;
            default:
                character.SendMessage("[Quest] /quest <add/remove/list/prog/reward/resetdaily>\nBefore that, target the Npc you need for the quest");
                break;
        }
    }
}
