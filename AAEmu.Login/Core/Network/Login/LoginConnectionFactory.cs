using System.Collections.Concurrent;
using AAEmu.Login.Core.Network.Connections;
using Microsoft.AspNetCore.Connections;

namespace AAEmu.Login.Core.Network.Login;

public class LoginConnectionFactory(
    IEnumerable<ILoginPacketDescriptor> packetDescriptors,
    ILoginProtocolHandler protocolHandler,
    IConnectionIdLeaseFactory connectionIdLeaseFactory,
    ILoginSessionFactory sessionFactory,
    ILoggerFactory loggerFactory) : ILoginConnectionFactory
{
    private readonly ConcurrentDictionary<ushort, ILoginPacketDescriptor> _packets =
        new(packetDescriptors.ToDictionary(d => d.TypeId));

    public ILoginConnectionOwner Create(ConnectionContext connectionContext)
    {
        var connectionIdLease = connectionIdLeaseFactory.Rent();
        try
        {
            var connection = new LoginConnection(connectionContext, connectionIdLease, _packets, protocolHandler,
                loggerFactory.CreateLogger<LoginConnection>());

            // Create and attach the session to manage the login state machine
            var session = sessionFactory.Create(connection);
            connection.SetSession(session);

            return connection;
        }
        catch
        {
            connectionIdLease.Dispose();
            throw;
        }
    }
}
