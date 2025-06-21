using AAEmu.Commons.Network;
using AAEmu.Login.Core.PacketHandlers;

namespace AAEmu.Login.Core.Network.Login;

public interface ILoginProtocolHandler : IBaseProtocolHandler
{
    void RegisterPacket<TPacket>(uint type, ILoginPacketHandler<TPacket> packetHandler) where TPacket : LoginPacket;
}
