using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

public class CARequestAuthMailRuPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestAuthMailRuPacket;
    
    public string? Id { get; private set; }
    public byte[]? Token { get; private set; }

    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var dev = stream.ReadBoolean();
        var mac = stream.ReadBytes();
        Id = stream.ReadString();
        Token = stream.ReadBytes();
    }
}
