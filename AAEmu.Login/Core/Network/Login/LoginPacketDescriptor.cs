using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers;

namespace AAEmu.Login.Core.Network.Login;

public class LoginPacketDescriptor<TPacket>(ushort packetId, ILoginPacketHandler<TPacket> handler)
    : ILoginPacketDescriptor
    where TPacket : LoginPacket, new()
{
    public ushort TypeId { get; } = packetId;

    public void Dispatch(PacketStream stream, LoginConnection connection)
    {
        var packet = new TPacket { Connection = connection };
        packet.Decode(stream);
        handler.Execute(packet, connection);
    }
}
