using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.PacketHandlers;

public interface ILoginPacketHandler<in TPacket> : IPacketHandler<TPacket, LoginConnection>,
    ILoginPacketHandler where TPacket : LoginPacket
{
    void ILoginPacketHandler.Execute(LoginPacket packet, LoginConnection connection)
    {
        Execute((TPacket)packet, connection);
    }
}
