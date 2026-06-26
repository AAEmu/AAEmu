using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C;

/// <summary>
/// A heartbeat packet sent by the login server to the client. The client replies with a
/// <see cref="AAEmu.Login.Core.Packets.C2L.CAPongPacket"/> echoing the same value.
/// </summary>
/// <param name="send">An opaque timestamp the client echoes back in its pong.</param>
public class ACPingPacket(ulong send) : LoginPacket(LCOffsets.ACPingPacket)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(send);
        return stream;
    }
}