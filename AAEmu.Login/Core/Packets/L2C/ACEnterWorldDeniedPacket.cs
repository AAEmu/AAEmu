using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A packet sent by the login server to the client to deny entry into the game world.
/// </summary>
/// <param name="reason">The reason entry to the game world is denied.</param>
public class ACEnterWorldDeniedPacket(byte reason) : LoginPacket(LCOffsets.ACEnterWorldDeniedPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(reason);

        return stream;
    }
}
