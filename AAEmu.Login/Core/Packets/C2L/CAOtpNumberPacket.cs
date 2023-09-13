using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CAOtpNumberPacket : LoginPacket
    {
        public CAOtpNumberPacket() : base(CLOffsets.CAOtpNumberPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var num = stream.ReadString(); // TODO but on old client length const 8
        }
    }
}
