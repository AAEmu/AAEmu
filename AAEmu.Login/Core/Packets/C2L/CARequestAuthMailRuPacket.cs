using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

public class CARequestAuthMailRuPacket() : LoginPacket(CLOffsets.CARequestAuthMailRuPacket)
{
    private string? _id;
    private byte[]? _token;
    
    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var dev = stream.ReadBoolean();
        var mac = stream.ReadBytes();
        _id = stream.ReadString();
        _token = stream.ReadBytes();
    }

    public override void Execute()
    {
        LoginController.Login(Connection, _id!, _token);
    }
}
