using System.Net;
using AAEmu.Commons.Models;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

/// <summary>
/// Represents a connection between a client and the login server.
/// </summary>
public interface ILoginConnection
{
    ConnectionId Id { get; }
    IPAddress Ip { get; }

    /// <summary>
    /// Triggered when the client connection is closed.
    /// </summary>
    CancellationToken ConnectionClosed { get; }

    bool IsLocallyConnected { get; }
    AccountId AccountId { get; set; }

    ValueTask SendPacketAsync(LoginPacket packet, CancellationToken cancellationToken);
    ValueTask SendPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken);
    void Shutdown();
    List<LoginCharacterInfo> GetCharacters();
    void AddCharacters(GameServerId gsId, List<LoginCharacterInfo> characterInfos);
}
