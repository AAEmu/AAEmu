using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

public class CACancelEnterWorldPacket() : LoginPacket(CLOffsets.CACancelEnterWorldPacket)
{
    public byte WorldId { get; private set; }
    
    public override void Read(PacketStream stream)
    {
        WorldId = stream.ReadByte(); // diw -> world id
    }
}
