using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSHeroRequestRankDataPacket : GamePacket
    {
        public CSHeroRequestRankDataPacket() : base(CSOffsets.CSHeroRequestRankDataPacket, 5)
        {
        }

        public override void Read(PacketStream stream)
        {
            _log.Debug("CSHeroRequestRankDataPacket");
        }
    }
}
