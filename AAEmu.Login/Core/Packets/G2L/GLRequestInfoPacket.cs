using AAEmu.Commons.Models;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLRequestInfoPacket() : InternalPacket(GLOffsets.GLRequestInfoPacket)
{
    public ConnectionId ConnectionId { get; private set; }
    public uint RequestId { get; private set; }
    public List<LoginCharacterInfo>? Characters { get; private set; }

    public override void Read(PacketStream stream)
    {
        ConnectionId = new ConnectionId(stream.ReadUInt32());
        RequestId = stream.ReadUInt32();
        Characters = stream.ReadCollection<LoginCharacterInfo>();
    }
}
