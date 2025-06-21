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
        services.AddSingleton<IInternalPacketHandler<GLRegisterGameServerPacket>, GLRegisterGameServerPacketHandler>();
        services.AddSingleton<IInternalPacketHandler<GLRequestInfoPacket>, GLRequestInfoPacketHandler>();
        services.AddSingleton<IInternalPacketHandler<GLPlayerReconnectPacket>, GLPlayerReconnectPacketHandler>();
        services.AddSingleton<IInternalPacketHandler<GLPlayerEnterPacket>, GLPlayerEnterPacketHandler>();
        services.AddSingleton<IInternalPacketHandler<GLGameServerLoadPacket>, GLGameServerLoadPacketHandler>();
    }
    
    public static void AddLoginPacketHandlers(this IServiceCollection services)
    {
        services.AddSingleton<ILoginPacketHandler<CACancelEnterWorldPacket>, CACancelEnterWorldPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CAChallengeResponse2Packet>, CAChallengeResponse2PacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CAChallengeResponsePacket>, CAChallengeResponsePacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CAEnterWorldPacket>, CAEnterWorldPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CAListWorldPacket>, CAListWorldPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CAOtpNumberPacket>, CAOtpNumberPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CAPcCertNumberPacket>, CAPcCertNumberPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CARequestAuthGameOnPacket>, CARequestAuthGameOnPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CARequestAuthMailRuPacket>, CARequestAuthMailRuPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CARequestAuthPacket>, CARequestAuthPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CARequestAuthTencentPacket>, CARequestAuthTencentPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CARequestAuthTrionPacket>, CARequestAuthTrionPacketHandler>();
        services.AddSingleton<ILoginPacketHandler<CARequestReconnectPacket>, CARequestReconnectPacketHandler>();
    }
}
