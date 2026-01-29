using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.C2L;

namespace AAEmu.Login.Core.PacketHandlers.C2L;

/// <summary>
/// Handles the <see cref="CACancelEnterWorldPacket"/> which is sent by the client when a player cancels the process of
/// entering the game world.
/// </summary>
public class CACancelEnterWorldPacketHandler : ILoginPacketHandler<CACancelEnterWorldPacket>
{
    public Task Execute(CACancelEnterWorldPacket packet, ILoginConnection connection,
        CancellationToken cancellationToken) => Task.CompletedTask;
}
