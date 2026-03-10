using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Controllers;

public interface IGameController
{
    bool TryGetParentId(GameServerId gsId, out GameServerId id);
    void Load();
    void Add(GameServerId gsId, List<GameServerId> mirrorsId, InternalConnection connection);
    void Remove(GameServerId gsId);

    /// <summary>
    /// Requests the list of available game worlds from the specified login connection.
    /// </summary>
    /// <param name="connection">The client connection making the request.</param>
    Task<WorldListResult> GetWorldListAsync(ILoginConnection connection);
    void SetLoad(GameServerId gsId, byte load);

    /// <summary>
    /// Gets a game server by its ID, or null if not found.
    /// </summary>
    GameServer? GetGameServer(GameServerId gsId);

    /// <summary>
    /// Sends an enter world request to the game server (fire-and-forget).
    /// The response will be routed via <see cref="RouteEnterWorldResponse"/>.
    /// </summary>
    void RequestEnterWorld(AccountId accountId, ConnectionId connectionId, GameServerId gsId);

    /// <summary>
    /// Routes an enter world response from a game server to the appropriate connection's session.
    /// </summary>
    void RouteEnterWorldResponse(ConnectionId connectionId, GameServerId gsId, byte result);
}
