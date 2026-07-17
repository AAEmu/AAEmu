using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client to the login server to request authentication (opcode 0x003).
/// </summary>
public class CARequestAuthGameOnPacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CARequestAuthPacket_0x003;

    public override void Read(PacketStream stream)
    {
        var pFrom = stream.ReadUInt32();
        var pTo = stream.ReadUInt32();
        var dev = stream.ReadBoolean();
        var qqno = stream.ReadUInt32();
        var len = stream.ReadUInt16();
        var sig = stream.ReadBytes(128);
        var key = stream.ReadBytes(16);
        var mac = stream.ReadBytes(8);
        var worldId = stream.ReadByte();
        var netbarSig = stream.ReadString();
    }
}
