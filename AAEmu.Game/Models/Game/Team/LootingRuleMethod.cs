namespace AAEmu.Game.Models.Game.Team;

public enum LootingRuleMethod : byte
{
    FreeForAll = 0,
    RotateWinner = 1,
    LootMaster = 2,
    Public = 0xFF // used internally
}
