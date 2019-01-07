using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CAChallengeResponsePacket : LoginPacket
    {
        public CAChallengeResponsePacket() : base(0x05)
        {}

        public override void Read(PacketStream stream)
        {
            for (var i = 0; i < 4; i++)
                stream.ReadUInt32(); // hc
            var wp = stream.ReadString(); // TODO or bytes? length 32 
        }
    }
}