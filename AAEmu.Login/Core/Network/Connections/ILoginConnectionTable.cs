using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Network.Connections;

public interface ILoginConnectionTable
{
    void AddConnection(LoginConnection con);
    LoginConnection? GetConnection(ConnectionId id);
    LoginConnection? RemoveConnection(ConnectionId id);
    List<LoginConnection> GetConnections();
}
