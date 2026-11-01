using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests.Scripts;

/// <summary>
/// Acceptance and persistence policy for The Nymph Queen's Rage.
/// Active encounters belong to the NPC spawner, never to a character.
/// </summary>
public sealed class NymphQueenQuestScript(Lazy<IQuestManager> questManager, ITaskManager taskManager,
    ITeamManager teamManager, TimeProvider clock, NymphQueenEncounterConfig config) : IQuestScript
{
    public uint QuestId => NymphQueenEncounter.QuestId;

    public bool TryAccept(QuestAcceptance request, Func<QuestAcceptance, bool> accept)
    {
        if (!request.Forcibly && (request.AcceptorType != QuestAcceptorType.Npc ||
            request.AcceptorId != NymphQueenEncounter.LympherodaId))
            return false;

        var player = request.Owner;
        if (request.Forcibly && player.Quests.HasQuest(QuestId))
            player.Quests.DropQuest(QuestId, true);

        var npc = NymphQueenEncounter.FindQuestNpc(player, request.Forcibly);
        if (npc?.Spawner == null)
        {
            player.SendMessage("The Nymph Queen is unavailable. Wait for the encounter to reset and try again.");
            return false;
        }

        player.CurrentTarget = npc;
        return NymphQueenEncounter.AcceptQuest(player, npc,
            () => accept(request with { AcceptorType = QuestAcceptorType.Npc, AcceptorId = npc.TemplateId }),
            questManager.Value, taskManager, teamManager, clock, config);
    }

    public void OnLoaded(Quest quest)
    {
        // Character selection reloads quests before sending them to the client.
        // A persisted quest cannot resume its previous runtime NPC encounter.
        NymphQueenEncounter.RecoverInterruptedQuest(quest);
    }

    public void OnRemoved(Quest quest, bool update)
    {
        if (update && !quest.Owner.Quests.HasQuestCompleted(QuestId))
            quest.Owner.Quests.SendQuestReset(QuestId);
    }
}
