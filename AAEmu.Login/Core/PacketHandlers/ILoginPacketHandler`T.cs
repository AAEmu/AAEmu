using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.PacketHandlers;

public interface ILoginPacketHandler<in TPacket> : IPacketHandler<TPacket, ILoginSession>,
    ILoginPacketHandler where TPacket : LoginPacket
{
    Task ILoginPacketHandler.Execute(LoginPacket packet, ILoginSession session,
        CancellationToken cancellationToken) => Execute((TPacket)packet, session, cancellationToken);
}
