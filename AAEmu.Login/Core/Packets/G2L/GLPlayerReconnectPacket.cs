using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Internal;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.G2L;

public class GLPlayerReconnectPacket() : InternalPacket(GLOffsets.GLPlayerReconnectPacket)
{
    private GameServerId _gsId;
    private AccountId _accountId;
    private uint _token;
    
    public override void Read(PacketStream stream)
    {
        _gsId = new GameServerId(stream.ReadByte());
        _accountId = new AccountId(stream.ReadUInt32());
        _token = stream.ReadUInt32();
    }

    public override void Execute()
    {
        LoginController.Instance.AddReconnectionToken(Connection, _gsId, _accountId, _token);
    }
}
