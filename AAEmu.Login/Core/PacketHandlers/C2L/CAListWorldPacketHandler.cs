using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CAListWorldPacket"/> which is sent by the client to request the list of available game
/// worlds.
/// </summary>
public class CAListWorldPacketHandler(IGameController gameController) : ILoginPacketHandler<CAListWorldPacket>
{
    public void Execute(CAListWorldPacket packet, LoginConnection connection)
    {
        Task.Run(() => gameController.RequestWorldListAsync(connection));
    }
}
