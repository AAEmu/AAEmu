using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;

namespace AAEmu.Login.Core.PacketHandlers;

public interface IInternalPacketHandler
{
    void Execute(InternalPacket packet, InternalConnection connection);
}
