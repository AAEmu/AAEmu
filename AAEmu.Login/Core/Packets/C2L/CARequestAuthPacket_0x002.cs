using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthPacket_0x002 : LoginPacket
    {
        public CARequestAuthPacket_0x002() : base(CLOffsets.CARequestAuthPacket_0x002)
        {
        }

        public override void Read(PacketStream stream)
        {
            var salt = stream.ReadUInt32();
            for (var i = 0; i < 4; i++)
                stream.ReadUInt32(); // ch
        }
    }
}
