using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.C2L;

public class CARequestReconnectPacket() : LoginPacket(CLOffsets.CARequestReconnectPacket)
{
    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var accountId = new AccountId(stream.ReadUInt32());
        var gsId = new GameServerId(stream.ReadByte());
        var cookie = stream.ReadUInt32();
        var macLength = stream.ReadUInt16();
        var mac = stream.ReadBytes(macLength);

        LoginController.Instance.Reconnect(Connection, gsId, accountId, cookie);
    }
}
