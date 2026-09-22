namespace AAEmu.Game.Models.Game.Quests;

/// <summary>
/// A Reward act whose grant needs a subsystem the server does not have. QuestRewardSupportRules
/// refuses the accept of its quest; a quest accepted before that gate existed, or forced by a GM,
/// still reaches the act, which then reports once per type and lets the step finish.
/// </summary>
public interface IUnsupportedRewardAct
{
    string MissingSubsystem { get; }
}
