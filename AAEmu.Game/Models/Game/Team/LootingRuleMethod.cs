namespace AAEmu.Game.Models.Game.Team;

public enum LootingRuleMethod : sbyte
{
    FreeForAll = 0,
    RotateWinner = 1,
    LootMaster = 2,
    Public = -1 // used internally
}
