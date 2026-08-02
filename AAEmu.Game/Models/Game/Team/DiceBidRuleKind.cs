namespace AAEmu.Game.Models.Game.Team;

/// <summary>
/// Per-member response used when a team item is offered for a dice roll.
/// </summary>
public enum DiceBidRuleKind : sbyte
{
    Default = 1,
    AutoAccept = 2,
    AutoGiveUp = 3
}
