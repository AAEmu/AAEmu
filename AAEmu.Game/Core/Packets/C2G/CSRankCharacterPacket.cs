using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRankCharacterPacket : GamePacket
    {
        public CSRankCharacterPacket() : base(CSOffsets.CSRankCharacterPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("RankCharacter");
        }
    }
}
