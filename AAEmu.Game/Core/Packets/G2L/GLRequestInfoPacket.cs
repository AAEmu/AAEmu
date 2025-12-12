using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L;

public class GLRequestInfoPacket(uint connectionId, uint requestId, List<LoginCharacterInfo> characters)
    : LoginPacket(GLOffsets.GLRequestInfoPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(connectionId);
        stream.Write(requestId);
        stream.Write(characters);
        return stream;
    }
}
