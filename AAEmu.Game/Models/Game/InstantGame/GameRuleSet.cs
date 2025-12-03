namespace AAEmu.Game.Models.Game.InstantGame
{
    public class GameRuleSet
    {
        public uint Id { get; set; }
        public uint CorpsSize { get; set; }
        public uint Corps1FactionId { get; set; }
        public uint Corps2FactionId { get; set; }
        public int TimeOpening { get; set; }
        public int TimeEnding { get; set; }
        public int TimePlaying { get; set; }
        public int VictoryScore { get; set; }
        public uint BattlefieldId { get; set; }
        // TODO fill out the rest of the fields
    }
}
