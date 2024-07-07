using System;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using Discord;

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
                    var acceptorType = QuestAcceptorType.Unknown;
                    var acceptorId = 0u;
                    if (args.Length >= 3)
                    {
                        if (Enum.TryParse<QuestAcceptorType>(args[2], out var acceptorTypeVal))
                            acceptorType = acceptorTypeVal;
                    }
                    if (args.Length >= 4)
                    {
                        if (uint.TryParse(args[3], out var acceptorIdVal))
                            acceptorId = acceptorIdVal;
                    }
                    else
                    {
                        if ((acceptorType == QuestAcceptorType.Npc) && (character.CurrentTarget is Npc npc))
                            acceptorId = npc.TemplateId;
                    }
                    if (uint.TryParse(args[1], out questId))
                    {
                        character.Quests.AddQuest(questId, true, acceptorType, acceptorId);
                        if (character.Quests.ActiveQuests.TryGetValue(questId, out var newQuest))
                        {
                            newQuest.RequestEvaluation();
                        }
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
                    character.SendMessage($"Quest {quest.Template.Id}: Step({quest.Step}), Status({quest.Status}), ComponentId({quest.ComponentId}), Objectives({string.Join(", ", objectives)}) - @QUEST_NAME({quest.Template.Id})");
                }
                break;
            case "reward":
                if (args.Length >= 2)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        if (args.Length >= 3 && int.TryParse(args[2], out var selectedId))
                        {
                            character.Quests.SetStep(questId, 8, selectedId);
                        }
                        else
                        {
                            character.Quests.SetStep(questId, 8);
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
                            if (args.Length >= 3 && Enum.TryParse<QuestComponentKind>(args[2], out var stepId))
                            {
                                if (character.Quests.SetStep(questId, (uint)stepId))
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
                            // quest.Update();
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
                            character.Quests.DropQuest(questId, true, true); // удаляем и из CompletedQuests
                        }
                        else
                        {
                            character.SendMessage($"[Quest] You do not have the quest {questId}");
                        }
                        // посылаем пакеты для того, что-бы клиент был в курсе обновления квестов
                        character.Quests.Send();
                        character.Quests.SendCompleted();
                    }
                }
                else
                {
                    character.SendMessage("[Quest] Proper usage: /quest remove <questId>");
                }
                break;
            case "uncomplete":
                // Drops a quest if active, and completely removes it, as if it was never started
                if (args.Length >= 2)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        if (character.Quests.HasQuest(questId))
                        {
                            character.Quests.DropQuest(questId, true, true); // удаляем и из CompletedQuests
                        }

                        if (character.Quests.IsQuestComplete(questId))
                        {
                            character.Quests.SetCompletedQuestFlag(questId, false);
                        }

                        character.Quests.Send();
                        character.Quests.SendCompleted();
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
            case "debug":
                if (args.Length >= 8)
                {
                    if (!uint.TryParse(args[1], out var questVal))
                        break;
                    if (!character.Quests.ActiveQuests.TryGetValue(questVal, out var activeQuest))
                    {
                        character.SendMessage(ChatType.System, $"[Quest] No active quest Id {questVal}", Color.Red);
                        break;
                    }
                    if (!int.TryParse(args[2], out var para1))
                        break;
                    if (!int.TryParse(args[3], out var para2))
                        break;
                    if (!int.TryParse(args[4], out var para3))
                        break;
                    if (!int.TryParse(args[5], out var para4))
                        break;
                    if (!int.TryParse(args[6], out var para5))
                        break;
                    if (!Enum.TryParse<QuestStatus>(args[7], out var status))
                        break;
                    if (!uint.TryParse(args[8], out var comp))
                        break;
                    activeQuest.Objectives[0] = para1;
                    activeQuest.Objectives[1] = para2;
                    activeQuest.Objectives[2] = para3;
                    activeQuest.Objectives[3] = para4;
                    activeQuest.Objectives[4] = para5;
                    activeQuest.Status = status;
                    activeQuest.ComponentId = comp;
                    character.SendPacket(new SCQuestContextUpdatedPacket(activeQuest,  activeQuest.ComponentId));
                }
                else
                {
                    character.SendMessage("[Quest] /quest debug <questId> <obj1> <obj2> <obj3> <obj4> <obj5> <status> <component>");
                }
                break;
            case "template":
                if (args.Length >= 2)
                {
                    if (!uint.TryParse(args[1], out var questVal))
                        break;

                    var questTemplate = QuestManager.Instance.GetTemplate(questVal);
                    if (questTemplate == null)
                    {
                        character.SendMessage(ChatType.System, $"[Quest] No such quest {questVal}", Color.Red);
                        break;
                    }

                    character.SendMessage($"[Quest] {questVal} -- Template");
                    foreach (var (componentId, componentTemplate) in questTemplate.Components)
                    {
                        character.SendMessage($"-- Component({componentId}), Step {componentTemplate.KindId}");
                        foreach (var actTemplate in componentTemplate.ActTemplates)
                        {
                            character.SendMessage($"---- Act({actTemplate.ActId}) => {actTemplate.DetailType}({actTemplate.DetailId})");
                        }
                    }
                    character.SendMessage($"[Quest] {questVal} -- End of Template");
                    
                    if (character.Quests.ActiveQuests.TryGetValue(questVal, out var activeQuest))
                    {
                        character.SendMessage($"[Quest] {questVal} -- Active");
                        foreach (var (stepId, step) in activeQuest.QuestSteps)
                        {
                            character.SendMessage($"Step {stepId}");
                            foreach (var (componentId, component) in step.Components)
                            {
                                character.SendMessage($"-- Component({componentId})");
                                foreach (var act in component.Acts)
                                {
                                    character.SendMessage($"---- Act({act.Id}) => {act.DetailType}({act.DetailId})");
                                }
                            }
                        }
                        character.SendMessage($"[Quest] {questVal} -- End of Active Quest");
                    }
                    else
                    {
                        character.SendMessage($"[Quest] {questVal} is not active");
                    }

                }
                else
                {
                    character.SendMessage("[Quest] /quest template <questId>");
                }
                break;
            default:
                character.SendMessage("[Quest] /quest <add/remove/list/prog/reward/resetdaily>\nBefore that, target the Npc you need for the quest");
                break;
        }
    }
}
