using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.L2C
{
    public class ACChallengePacket : LoginPacket
    {
        public ACChallengePacket() : base(LCOffsets.ACChallengePacket)
        {
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((uint) 0); // salt
            for (var i = 0; i < 4; i++)
                stream.Write((uint) 0); // hc

            return stream;
        }
    }
}
