using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L;

public class GLPlayerReconnectPacket(byte gsId, ulong accountId, uint connectionId)
    : LoginPacket(GLOffsets.GLPlayerReconnectPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(gsId);
        stream.Write(accountId);
        stream.Write(connectionId);
        return stream;
    }
}
