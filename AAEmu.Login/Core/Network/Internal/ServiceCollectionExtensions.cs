using AAEmu.Login.Core.Network.Connections;

namespace AAEmu.Login.Core.Network.Internal;

public static class ServiceCollectionExtensions
{
    public static void AddInternalNetwork(this IServiceCollection services)
    {
        services
            .AddSingleton<IInternalProtocolHandler, InternalProtocolHandler>()
            .AddSingleton<IInternalConnectionTable, InternalConnectionTable>()
            .AddSingleton<IInternalNetwork, InternalNetwork>();
    }
}
