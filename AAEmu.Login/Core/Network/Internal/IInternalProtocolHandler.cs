using AAEmu.Commons.Network;
using AAEmu.Login.Core.PacketHandlers;

namespace AAEmu.Login.Core.Network.Internal;

public interface IInternalProtocolHandler : IBaseProtocolHandler
{
    void RegisterPacket<TPacket>(uint type, IInternalPacketHandler<TPacket> packetHandler)
        where TPacket : InternalPacket;
}
