using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Packets.G2L;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

/// <summary>
/// Handles the <see cref="GLRequestInfoPacket"/> which is sent by the game server in response to a request for
/// information about an account's characters made by the login server.
/// </summary>
public class GLRequestInfoPacketHandler(
    IRequestController requestController,
    ILoginConnectionTable loginConnectionTable)
    : IInternalPacketHandler<GLRequestInfoPacket>
{
    public void Execute(GLRequestInfoPacket packet, InternalConnection connection)
    {
        var loginConnection = loginConnectionTable.GetConnection(packet.ConnectionId);
        if (packet.Characters!.Count > 0)
            loginConnection!.AddCharacters(connection.GameServer!.Id, packet.Characters);
        requestController.ReleaseId(packet.RequestId);
    }
}
