using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Models.Game.InstantGame
{
    public class BattlefieldSpawns
    {
        public uint BattlefieldId { get; set; }
        public Point Corps1Spawn { get; set; }
        public Point Corps2Spawn { get; set; }
    }
}
