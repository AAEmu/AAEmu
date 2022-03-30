using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthPacket_0x016 : LoginPacket
    {
        public CARequestAuthPacket_0x016() : base(CLOffsets.CARequestAuthPacket_0x016)
        {
        }

        public override void Read(PacketStream stream)
        {
            // nullsub_660
        }
    }
}
