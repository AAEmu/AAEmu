using System;
using System.Linq;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Quests.Static;
using AAEmu.Game.Utils.Scripts;
using Discord;

namespace AAEmu.Game.Utils;

public class QuestCommandUtil
{
    public static void GetCommandChoice(ICommand command, IMessageOutput messageOutput, ICharacter character, string choice, string[] args)
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
                    CommandManager.SendErrorText(command, messageOutput,"Proper usage: /quest add <questId> [<questAcceptorType> <acceptorId>]\nYou can also target the quest giver instead of providing the acceptor type and id");
                }
                break;
            case "list":
                CommandManager.SendNormalText(command, messageOutput, "LIST");
                foreach (var quest in character.Quests.ActiveQuests.Values)
                {
                    var objectives = quest.GetObjectives(quest.Step).Select(t => t.ToString()).ToList();
                    messageOutput.SendMessage($"Quest {quest.Template.Id}: Step({quest.Step}), Status({quest.Status}), ComponentId({quest.ComponentId}), Objectives({string.Join(", ", objectives)}) - @QUEST_NAME({quest.Template.Id})");
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
                                    CommandManager.SendNormalText(command, messageOutput,
                                        $"Set Step {stepId} for Quest {questId}");
                                else
                                    CommandManager.SendErrorText(command, messageOutput, "Proper usage: /quest step <questId> <stepId>");
                            }
                        }
                        else
                        {
                            CommandManager.SendErrorText(command, messageOutput, $"You do not have the quest {questId}");
                        }
                    }
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "Proper usage: /quest step <questId> <stepId>");
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
                            CommandManager.SendNormalText(command, messageOutput, $"Perform step {quest.Step} for quest {questId}");
                            // quest.Update();
                        }
                        else
                        {
                            CommandManager.SendErrorText(command, messageOutput, $"You do not have the quest {questId}");
                        }
                    }
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "Proper usage: /quest update <questId>");
                }
                break;
            case "remove":
                if (args.Length >= 2)
                {
                    if (uint.TryParse(args[1], out questId))
                    {
                        if (character.Quests.HasQuest(questId))
                        {
                            character.Quests.DropQuest(questId, true, true); // Remove from CompletedQuests
                        }
                        else
                        {
                            CommandManager.SendErrorText(command, messageOutput, $"You do not have the quest {questId}");
                        }
                        // send packets to keep the client up-to-date on quests
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
                            character.Quests.DropQuest(questId, true, true);
                            CommandManager.SendNormalText(command, messageOutput, $"Quest @QUEST_NAME({questId}) ({questId}) has been dropped");
                        }

                        if (character.Quests.IsQuestComplete(questId))
                        {
                            character.Quests.SetCompletedQuestFlag(questId, false);
                            CommandManager.SendNormalText(command, messageOutput, $"Quest @QUEST_NAME({questId}) ({questId}) has been been marked as not completed. " +
                                $"You will need to log in your character again to correctly reflect these changes (return to character select)");
                        }

                        character.Quests.Send();
                        character.Quests.SendCompleted();
                    }
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "Proper usage: /quest remove <questId>");
                }
                break;
            case "resetdaily":
                character.Quests.ResetDailyQuests(true);
                CommandManager.SendNormalText(command, messageOutput, $"Your daily quests have been reset");
                break;
            case "objective":
                if (args.Length >= 8)
                {
                    if (!uint.TryParse(args[1], out var questVal))
                        break;
                    if (!character.Quests.ActiveQuests.TryGetValue(questVal, out var activeQuest))
                    {
                        CommandManager.SendErrorText(command, messageOutput, $"No active quest Id {questVal}");
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
                    CommandManager.SendErrorText(command, messageOutput, "/quest objective <questId> <obj1> <obj2> <obj3> <obj4> <obj5> <questStatus> <componentId>");
                }
                break;
            case "progress":
                if (args.Length >= 1)
                {
                    if (!uint.TryParse(args[1], out var questVal))
                        break;
                    if (!character.Quests.ActiveQuests.TryGetValue(questVal, out var activeQuest))
                    {
                        CommandManager.SendErrorText(command, messageOutput, $"No active quest Id {questVal}");
                        break;
                    }

                    var loopCounter = 0;
                    while ((activeQuest.Step < QuestComponentKind.Progress) && (loopCounter < 10))
                    {
                        loopCounter++;
                        var res = false;
                        if (activeQuest.QuestSteps.ContainsKey(activeQuest.Step))
                            res = activeQuest.RunCurrentStep();
                        if (!res)
                            activeQuest.GoToNextStep();
                    }
                    
                    activeQuest.Objectives[0] = 0;
                    activeQuest.Objectives[1] = 0;
                    activeQuest.Objectives[2] = 0;
                    activeQuest.Objectives[3] = 0;
                    activeQuest.Objectives[4] = 0;
                    activeQuest.Status = QuestStatus.Progress;
                    activeQuest.ComponentId = 0;
                    activeQuest.RequestEvaluation();
                    character.SendPacket(new SCQuestContextUpdatedPacket(activeQuest,  activeQuest.ComponentId));
                    CommandManager.SendNormalText(command, messageOutput, $"Reset objectives of quest @QUEST_NAME({activeQuest.TemplateId}) ({activeQuest.TemplateId}) and moved to {activeQuest.Step} step");
                }
                else
                {
                    CommandManager.SendErrorText(command, messageOutput, "/quest progress <questId>");
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
                CommandManager.SendDefaultHelpText(command, messageOutput);
                break;
        }
    }
}
