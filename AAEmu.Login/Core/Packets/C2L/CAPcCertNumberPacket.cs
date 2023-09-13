using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CAPcCertNumberPacket : LoginPacket
    {
        public CAPcCertNumberPacket() : base(CLOffsets.CAPcCertNumberPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var num = stream.ReadString(); // TODO but on old client length const 8
        }
    }
}
