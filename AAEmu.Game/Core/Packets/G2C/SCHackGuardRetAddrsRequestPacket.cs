using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHackGuardRetAddrsRequestPacket(bool sendAddrs, bool spMd5)
    : GamePacket(SCOffsets.SCHackGuardRetAddrsRequestPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(sendAddrs);
        stream.Write(spMd5);
        return stream;
    }
}
