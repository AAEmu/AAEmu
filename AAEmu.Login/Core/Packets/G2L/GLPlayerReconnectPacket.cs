using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLPlayerReconnectPacket() : InternalPacket(GLOffsets.GLPlayerReconnectPacket)
{
    public override void Read(PacketStream stream)
    {
        var gsId = new GameServerId(stream.ReadByte());
        var accountId = new AccountId(stream.ReadUInt32());
        var token = stream.ReadUInt32();

        LoginController.Instance.AddReconnectionToken(Connection, gsId, accountId, token);
    }
}
