using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLGameServerLoadPacket() : InternalPacket(GLOffsets.GLGameServerLoadPacket)
{
    private GSLoad _load;
    
    public override void Read(PacketStream stream)
    {
        _load = (GSLoad)stream.ReadByte();
    }

    public override void Execute()
    {
        Connection.GameServer!.Load = _load;
    }
}
