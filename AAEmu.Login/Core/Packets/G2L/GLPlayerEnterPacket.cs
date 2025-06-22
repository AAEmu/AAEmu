using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLPlayerEnterPacket() : InternalPacket(TypeId), IInternalPacket
{
    public new static ushort TypeId => GLOffsets.GLPlayerEnterPacket;
    
    public ConnectionId ConnectionId { get; private set; }
    public GameServerId GsId { get; private set; }
    public byte Result { get; private set; }

    public override void Read(PacketStream stream)
    {
        ConnectionId = new ConnectionId(stream.ReadUInt32());
        GsId = new GameServerId(stream.ReadByte());
        Result = stream.ReadByte();
    }
}
