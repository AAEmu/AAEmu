using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CAPcCertNumberPacketHandler
    : ILoginPacketHandler<CAPcCertNumberPacket>
{
    public Task ExecuteAsync(CAPcCertNumberPacket packet, LoginConnection connection) => Task.CompletedTask;
}
