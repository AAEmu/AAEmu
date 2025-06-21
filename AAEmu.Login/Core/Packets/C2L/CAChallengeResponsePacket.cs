using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L;

public class CAChallengeResponsePacket() : LoginPacket(CLOffsets.CAChallengeResponsePacket)
{
    public override void Read(PacketStream stream)
    {
        for (var i = 0; i < 4; i++)
            stream.ReadUInt32(); // responses
        var password = stream.ReadBytes(); // TODO or bytes? length 32
        var bytes = Convert.FromBase64String("jZae727K08KaOmKSgOaGzww/XVqGr/PKEgIMkjrcbJI=");
    }
}
