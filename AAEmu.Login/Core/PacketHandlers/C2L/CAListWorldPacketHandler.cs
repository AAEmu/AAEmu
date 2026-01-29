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
    public async Task Execute(CAListWorldPacket packet, ILoginSession session,
        CancellationToken cancellationToken)
    {
        var worldList = await gameController.GetWorldListAsync(session.Connection);

        await session.Client.SendWorldListAsync(worldList, cancellationToken);
    }
}
