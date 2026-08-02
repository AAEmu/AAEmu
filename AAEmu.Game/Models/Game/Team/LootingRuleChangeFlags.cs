namespace AAEmu.Game.Models.Game.Team;

[Flags]
public enum LootingRuleChangeFlags : sbyte
{
    None = 0,
    Method = 1 << 0,
    MinimumGrade = 1 << 1,
    LootMaster = 1 << 2,
    RollForBindOnPickup = 1 << 3,
    All = Method | MinimumGrade | LootMaster | RollForBindOnPickup
}
