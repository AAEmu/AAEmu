using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CAOtpNumberPacketHandler
    : ILoginPacketHandler<CAOtpNumberPacket>
{
    public Task ExecuteAsync(CAOtpNumberPacket packet, LoginConnection connection) => Task.CompletedTask;
}
