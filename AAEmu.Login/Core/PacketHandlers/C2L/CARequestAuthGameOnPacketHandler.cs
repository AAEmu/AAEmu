using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CARequestAuthGameOnPacket"/> which is sent by the client to request authentication for
/// entering the game world.
/// </summary>
public class CARequestAuthGameOnPacketHandler : ILoginPacketHandler<CARequestAuthGameOnPacket>
{
    public Task Execute(CARequestAuthGameOnPacket packet, ILoginSession session,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
