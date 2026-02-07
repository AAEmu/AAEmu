using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Models;
using AAEmu.Login.Utils;

namespace AAEmu.Login.Core.Network.Login;

public static class ServiceCollectionExtensions
{
    public static void AddLoginNetwork(this IServiceCollection services)
    {
        services
            .AddSingleton<ILoginProtocolHandler, LoginProtocolHandler>()
            .AddSingleton<ILoginConnectionTable, LoginConnectionTable>()
            .AddSingleton<ILoginConnectionFactory, LoginConnectionFactory>()
            .AddSingleton<IConnectionIdLeaseFactory, ConnectionIdLeaseFactory>()
            .AddSingleton<IIdManager<ConnectionId>, SimpleIdManager<ConnectionId>>(_ =>
                new SimpleIdManager<ConnectionId>(id => new ConnectionId(id), connectionId => connectionId.Value));
    }
}
