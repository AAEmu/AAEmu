using System;
using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.Packets.L2C;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CAChallengeResponsePacket : LoginPacket
    {
        public CAChallengeResponsePacket() : base(CLOffsets.CAChallengeResponsePacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            for (var i = 0; i < 4; i++)
                stream.ReadUInt32(); // responses
            var password = stream.ReadBytes(); // TODO or bytes? length 32
            var bytes = Convert.FromBase64String("jZae727K08KaOmKSgOaGzww/XVqGr/PKEgIMkjrcbJI=");
            Connection.SendPacket(new ACLoginDeniedPacket(3));
        }
    }
}
