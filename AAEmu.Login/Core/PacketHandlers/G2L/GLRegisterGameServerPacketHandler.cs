using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.G2L;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

/// <summary>
/// Handles the <see cref="GLRegisterGameServerPacket"/> which is sent by the game server to register itself with the
/// login server.
/// </summary>
public class GLRegisterGameServerPacketHandler(IGameController gameController, IOptions<AppConfiguration> appConfig)
    : IInternalPacketHandler<GLRegisterGameServerPacket>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public Task Execute(GLRegisterGameServerPacket packet, InternalConnection connection,
        CancellationToken cancellationToken)
    {
        if (packet.SecretKey != appConfig.Value.SecretKey)
        {
            Logger.Error($"Connection {connection.Ip}, bad secret key");
            Task.Run(() => SendPacketWithDelay(5000, new LGRegisterGameServerPacket(GSRegisterResult.Error)),
                cancellationToken);
            // Connection.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Error));
            return Task.CompletedTask;
        }

        gameController.Add(packet.GsId, packet.Mirrors!, connection);
        return Task.CompletedTask;

        async Task SendPacketWithDelay(int delay, InternalPacket message)
        {
            await Task.Delay(delay, cancellationToken);
            connection.SendPacket(message);
        }
    }
}
