using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

public class CAListWorldPacketHandler(IGameController gameController)
    : ILoginPacketHandler<CAListWorldPacket>
{
    public void Execute(CAListWorldPacket packet, LoginConnection connection)
    {
        Task.Run(() => gameController.RequestWorldListAsync(connection));
    }
}
