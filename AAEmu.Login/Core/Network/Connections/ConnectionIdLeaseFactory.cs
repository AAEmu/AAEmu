using AAEmu.Login.Models;
using AAEmu.Login.Utils;

namespace AAEmu.Login.Core.Network.Connections;

public sealed class ConnectionIdLeaseFactory(IIdManager<ConnectionId> pool) : IConnectionIdLeaseFactory
{
    public ConnectionIdLease Rent()
    {
        var id = pool.Rent();
        return new ConnectionIdLease(id, pool);
    }
}
