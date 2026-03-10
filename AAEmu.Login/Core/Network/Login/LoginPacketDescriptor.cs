using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.PacketHandlers;

namespace AAEmu.Login.Core.Network.Login;

public class LoginPacketDescriptor<TPacket>(ushort packetId, ILoginPacketHandler<TPacket> handler)
    : ILoginPacketDescriptor
    where TPacket : LoginPacket, new()
{
    public ushort TypeId { get; } = packetId;

    public async Task Dispatch(PacketStream stream, ILoginSession session, CancellationToken cancellationToken)
    {
        var packet = new TPacket { Connection = session.Connection };
        packet.Decode(stream);
        await handler.Execute(packet, session, cancellationToken);
    }
}
