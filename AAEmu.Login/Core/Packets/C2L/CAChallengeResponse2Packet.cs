using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

public class CAChallengeResponse2Packet() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAChallengeResponse2Packet;
    
    public override void Read(PacketStream stream)
    {
        for (var i = 0; i < 8; i++)
            stream.ReadUInt32(); // hc
    }
}
