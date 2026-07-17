using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCRecoverableExpPacket(uint objId, int recoverableExp, int penaltiedExp, int reason)
    : GamePacket(SCOffsets.SCRecoverableExpPacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(objId);
        stream.Write(recoverableExp);
        stream.Write(penaltiedExp);
        stream.Write(reason);
        return stream;
    }
}
