using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Quests.Static;

namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// Optional quest-specific behavior outside the shared quest state machine.
/// </summary>
public interface IQuestScript
{
    uint QuestId { get; }
    bool TryAccept(QuestAcceptance request, Func<QuestAcceptance, bool> accept);
    void OnLoaded(Quest quest);
    void OnRemoved(Quest quest, bool update);
}

public sealed record QuestAcceptance(Character Owner, uint QuestId, bool Forcibly,
    QuestAcceptorType AcceptorType, uint AcceptorId);
