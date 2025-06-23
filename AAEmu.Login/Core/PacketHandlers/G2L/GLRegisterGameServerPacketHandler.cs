using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Connections;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.G2L;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;
using Microsoft.Extensions.Options;
using NLog;

namespace AAEmu.Login.Core.PacketHandlers.G2L;

public class GLRegisterGameServerPacketHandler(IGameController gameController, IOptions<AppConfiguration> appConfig)
    : IInternalPacketHandler<GLRegisterGameServerPacket>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    public void Execute(GLRegisterGameServerPacket packet, InternalConnection connection)
    {
        if (packet.SecretKey != appConfig.Value.SecretKey)
        {
            Logger.Error($"Connection {connection.Ip}, bad secret key");
            Task.Run(() => SendPacketWithDelay(5000, new LGRegisterGameServerPacket(GSRegisterResult.Error)));
            // Connection.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Error));
            return;
        }

        gameController.Add(packet.GsId, packet.Mirrors!, connection);
        return;

        async Task SendPacketWithDelay(int delay, InternalPacket message)
        {
            await Task.Delay(delay);
            connection.SendPacket(message);
        }
    }
}
