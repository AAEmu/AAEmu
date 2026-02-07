using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

public interface ILoginConnectionTable
{
    void AddConnection(ILoginConnection con);

    /// <summary>
    /// Gets the connection of a client to the login server by its connection ID.
    /// </summary>
    /// <param name="id">The unique identifier of the connection.</param>
    /// <returns>The connection, or null if there is no connection known by the specified ID.</returns>
    ILoginConnection? GetConnection(ConnectionId id);

    ILoginConnection? RemoveConnection(ConnectionId id);
    List<ILoginConnection> GetConnections();
}
