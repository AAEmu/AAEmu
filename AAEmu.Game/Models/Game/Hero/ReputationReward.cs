namespace AAEmu.Game.Models.Game.Hero;

/// <summary>One rung of the reputation_rewards ladder.</summary>
/// <param name="Percent">
/// Cumulative share of the ranked field this rung covers, counted from the top: 0.03 is "the best 3%".
/// The rungs are read in ascending order and the first one a player's percentile fits inside pays out,
/// so a rung's real span is from the previous rung's percent up to its own.
/// </param>
/// <param name="LeadershipPoint">Leadership paid to everyone in that band.</param>
public readonly record struct ReputationReward(double Percent, int LeadershipPoint);
