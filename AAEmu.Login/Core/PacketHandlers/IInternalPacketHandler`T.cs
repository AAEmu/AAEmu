using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.PacketHandlers;

public interface IInternalPacketHandler<in TPacket> : IPacketHandler<TPacket, InternalConnection>,
    IInternalPacketHandler where TPacket : InternalPacket
{
    void IInternalPacketHandler.Execute(InternalPacket packet, InternalConnection connection)
    {
        Execute((TPacket)packet, connection);
    }
}
