using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.PacketHandlers;

public interface ILoginPacketHandler<in TPacket> : IPacketHandler<TPacket, ILoginConnection>,
    ILoginPacketHandler where TPacket : LoginPacket
{
    Task ILoginPacketHandler.Execute(LoginPacket packet, ILoginConnection connection) =>
        Execute((TPacket)packet, connection);
}
