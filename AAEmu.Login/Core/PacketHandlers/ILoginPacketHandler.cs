using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.PacketHandlers;

public interface ILoginPacketHandler
{
    Task Execute(LoginPacket packet, ILoginConnection connection, CancellationToken cancellationToken);
}
