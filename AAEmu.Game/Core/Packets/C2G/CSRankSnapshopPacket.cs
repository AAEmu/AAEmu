using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G
{
    public class CSRankSnapshotPacket : GamePacket
    {
        public CSRankSnapshotPacket() : base(CSOffsets.CSRankSnapshotPacket, 1)
        {
        }

        public override void Read(PacketStream stream)
        {
            // Empty struct
            _log.Debug("RankSnapshot " + stream.Count);
        }
    }
}
