using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Core.Packets.L2G;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLRegisterGameServerPacket() : InternalPacket(GLOffsets.GLRegisterGameServerPacket)
{
    private string? _secretKey;
    private GameServerId _gsId;
    private List<GameServerId>? _mirrors;
    
    private async Task SendPacketWithDelay(int delay, InternalPacket message)
    {
        await Task.Delay(delay);
        Connection.SendPacket(message);
    }

    public override void Read(PacketStream stream)
    {
        _secretKey = stream.ReadString();
        _gsId = new GameServerId(stream.ReadByte());
        var additionalesCount = stream.ReadInt32();
        var mirrors = new List<GameServerId>(additionalesCount);
        for (var i = 0; i < additionalesCount; i++)
            mirrors.Add(new GameServerId(stream.ReadByte()));

        _mirrors = mirrors;
    }

    public override void Execute()
    {
        if (_secretKey != AppConfiguration.Instance.SecretKey)
        {
            Logger.Error($"Connection {Connection.Ip}, bad secret key");
            Task.Run(() => SendPacketWithDelay(5000, new LGRegisterGameServerPacket(GSRegisterResult.Error)));
            // Connection.SendPacket(new LGRegisterGameServerPacket(GSRegisterResult.Error));
            return;
        }
        
        GameController.Instance.Add(_gsId, _mirrors!, Connection);
    }
}
