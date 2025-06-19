using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.C2L;

public class CARequestReconnectPacket() : LoginPacket(CLOffsets.CARequestReconnectPacket)
{
    private GameServerId _gsId;
    private AccountId _accountId;
    private uint _cookie;
    
    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        _accountId = new AccountId(stream.ReadUInt32());
        _gsId = new GameServerId(stream.ReadByte());
        _cookie = stream.ReadUInt32();
        var macLength = stream.ReadUInt16();
        var mac = stream.ReadBytes(macLength);
    }

    public override void Execute()
    {
        LoginController.Instance.Reconnect(Connection, _gsId, _accountId, _cookie);
    }
}
