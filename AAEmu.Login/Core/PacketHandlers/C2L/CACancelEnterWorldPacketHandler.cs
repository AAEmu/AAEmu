using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CACancelEnterWorldPacketHandler
    : ILoginPacketHandler<CACancelEnterWorldPacket>
{
    public Task ExecuteAsync(CACancelEnterWorldPacket packet, LoginConnection connection) => Task.CompletedTask;
}
