using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCHouseSoldPacket(ushort tl, uint unkId, uint unk2Id, ulong newOwnerAcc, string ownerName, string houseName)
    : GamePacket(SCOffsets.SCHouseSoldPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(tl);
        stream.Write(unkId);
        stream.Write(unk2Id);
        stream.Write(newOwnerAcc);
        stream.Write(ownerName);
        stream.Write(houseName);
        return stream;
    }
}
