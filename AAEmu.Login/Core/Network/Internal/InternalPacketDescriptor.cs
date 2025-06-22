using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers;

namespace AAEmu.Login.Core.Network.Internal;

public class InternalPacketDescriptor<TPacket>(ushort packetId, IInternalPacketHandler<TPacket> handler)
    : IInternalPacketDescriptor
    where TPacket : InternalPacket, new()
{
    public ushort TypeId { get; } = packetId;

    public void Dispatch(PacketStream stream, InternalConnection connection)
    {
        var packet = new TPacket { Connection = connection };
        packet.Decode(stream);
        handler.Execute(packet, connection);
    }
}
