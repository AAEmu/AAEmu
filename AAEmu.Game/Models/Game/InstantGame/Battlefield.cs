namespace AAEmu.Game.Models.Game.InstantGame;

public class Battlefield
{
    public uint Id { get; set; }
    public uint ZoneKey { get; set; }
    public BattlefieldSpawns Spawns { get; set; }
    public GameRuleSet RuleSet { get; set; }
}