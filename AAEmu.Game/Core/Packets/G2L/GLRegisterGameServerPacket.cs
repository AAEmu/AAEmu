using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Login;

namespace AAEmu.Game.Core.Packets.G2L;

public class GLRegisterGameServerPacket(string secretKey, byte gsId, byte[] additionalesGsId)
    : LoginPacket(GLOffsets.GLRegisterGameServerPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(secretKey);
        stream.Write(gsId);
        stream.Write(additionalesGsId.Length);
        foreach (var gsId in additionalesGsId)
            stream.Write(gsId);
        return stream;
    }
}
