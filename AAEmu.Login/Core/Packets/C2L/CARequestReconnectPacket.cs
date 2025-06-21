using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Models;

namespace AAEmu.Login.Core.Packets.C2L;

public class CARequestReconnectPacket() : LoginPacket(CLOffsets.CARequestReconnectPacket)
{
    public GameServerId GsId { get; private set; }
    public AccountId AccountId { get; private set; }
    public uint Cookie { get; private set; }

    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        AccountId = new AccountId(stream.ReadUInt32());
        GsId = new GameServerId(stream.ReadByte());
        Cookie = stream.ReadUInt32();
        var macLength = stream.ReadUInt16();
        var mac = stream.ReadBytes(macLength);
    }
}
