using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSRankSnapshotPacket : GamePacket
{
    public CSRankSnapshotPacket() : base(CSOffsets.CSRankSnapshotPacket, 5)
    {
    }

    public override void Read(PacketStream stream)
    {
        Logger.Debug("CSRankSnapshotPacket");
    }
}
