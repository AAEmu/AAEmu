using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.PacketHandlers.C2L;
using AAEmu.Login.Core.PacketHandlers.G2L;
using AAEmu.Login.Core.Packets.C2L;
using AAEmu.Login.Core.Packets.G2L;
using Microsoft.Extensions.DependencyInjection;

namespace AAEmu.Login.Core.PacketHandlers;

public static class ServiceCollectionExtensions
{
    public static void AddInternalPacketHandlers(this IServiceCollection services)
    {
        services
            .AddInternalPacket<GLRegisterGameServerPacket, GLRegisterGameServerPacketHandler>()
            .AddInternalPacket<GLRequestInfoPacket, GLRequestInfoPacketHandler>()
            .AddInternalPacket<GLPlayerReconnectPacket, GLPlayerReconnectPacketHandler>()
            .AddInternalPacket<GLPlayerEnterPacket, GLPlayerEnterPacketHandler>()
            .AddInternalPacket<GLGameServerLoadPacket, GLGameServerLoadPacketHandler>();
    }

    public static void AddLoginPacketHandlers(this IServiceCollection services)
    {
        services
            .AddLoginPacket<CACancelEnterWorldPacket, CACancelEnterWorldPacketHandler>()
            .AddLoginPacket<CAChallengeResponse2Packet, CAChallengeResponse2PacketHandler>()
            .AddLoginPacket<CAChallengeResponsePacket, CAChallengeResponsePacketHandler>()
            .AddLoginPacket<CAEnterWorldPacket, CAEnterWorldPacketHandler>()
            .AddLoginPacket<CAListWorldPacket, CAListWorldPacketHandler>()
            .AddLoginPacket<CAOtpNumberPacket, CAOtpNumberPacketHandler>()
            .AddLoginPacket<CAPcCertNumberPacket, CAPcCertNumberPacketHandler>()
            .AddLoginPacket<CARequestAuthGameOnPacket, CARequestAuthGameOnPacketHandler>()
            .AddLoginPacket<CARequestAuthMailRuPacket, CARequestAuthMailRuPacketHandler>()
            .AddLoginPacket<CARequestAuthPacket, CARequestAuthPacketHandler>()
            .AddLoginPacket<CARequestAuthTencentPacket, CARequestAuthTencentPacketHandler>()
            .AddLoginPacket<CARequestAuthTrionPacket, CARequestAuthTrionPacketHandler>()
            .AddLoginPacket<CARequestReconnectPacket, CARequestReconnectPacketHandler>();
    }

    private static IServiceCollection AddLoginPacket<TPacket, TPacketHandler>(
        this IServiceCollection services)
        where TPacket : LoginPacket, ILoginPacket, new()
        where TPacketHandler : class, ILoginPacketHandler<TPacket>
    {
        services.AddSingleton<ILoginPacketHandler<TPacket>, TPacketHandler>();
        services.AddSingleton<ILoginPacketDescriptor>(sp =>
        {
            var handler = sp.GetRequiredService<ILoginPacketHandler<TPacket>>();
            return new LoginPacketDescriptor<TPacket>(TPacket.TypeId, handler);
        });

        return services;
    }

    private static IServiceCollection AddInternalPacket<TPacket, TPacketHandler>(
        this IServiceCollection services)
        where TPacket : InternalPacket, IInternalPacket, new()
        where TPacketHandler : class, IInternalPacketHandler<TPacket>
    {
        services.AddSingleton<IInternalPacketHandler<TPacket>, TPacketHandler>();
        services.AddSingleton<IInternalPacketDescriptor>(sp =>
        {
            var handler = sp.GetRequiredService<IInternalPacketHandler<TPacket>>();
            return new InternalPacketDescriptor<TPacket>(TPacket.TypeId, handler);
        });

        return services;
    }
}
