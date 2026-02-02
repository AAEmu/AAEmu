using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.Packets.L2C;

namespace AAEmu.Login.Core.Packets.C2L;

/// <summary>
/// A packet sent by the client in response to a challenge issued by the login server.
/// </summary>
/// <seealso cref="ACChallengePacket"/>
public class CAChallengeResponsePacket() : LoginPacket(TypeId), ILoginPacket
{
    public new static ushort TypeId => CLOffsets.CAChallengeResponsePacket;

    public byte[]? Password { get; private set; }

    public override void Read(PacketStream stream)
    {
        for (var i = 0; i < 4; i++)
            stream.ReadUInt32(); // responses
        Password = stream.ReadBytes(); // TODO or bytes? length 32
    }
}
