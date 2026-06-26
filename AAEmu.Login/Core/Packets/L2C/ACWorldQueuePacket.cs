using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to provide information about the world queue status.
/// </summary>
public class ACWorldQueuePacket() : LoginPacket(LCOffsets.ACWorldQueuePacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)0); // diw -> world id
        stream.Write((byte)0); // userGrade
        stream.Write((ushort)0); // myTurn
        stream.Write((ushort)0); // normalLength
        stream.Write((ushort)0); // premiumLength
        return stream;
    }
}
