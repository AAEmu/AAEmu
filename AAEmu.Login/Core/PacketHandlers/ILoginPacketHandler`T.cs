using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.PacketHandlers;

public interface ILoginPacketHandler<in TPacket> : IPacketHandler<TPacket, LoginConnection>,
    ILoginPacketHandler where TPacket : LoginPacket
{
    Task ILoginPacketHandler.ExecuteAsync(LoginPacket packet, LoginConnection connection)
    {
        return ExecuteAsync((TPacket)packet, connection);
    }
}
