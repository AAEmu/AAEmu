using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CARequestAuthTencentPacketHandler
    : ILoginPacketHandler<CARequestAuthTencentPacket>
{
    public Task ExecuteAsync(CARequestAuthTencentPacket packet, LoginConnection connection)
    {
        return Task.CompletedTask;
    }
}
