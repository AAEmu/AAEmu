using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthPacket : LoginPacket
    {
        public CARequestAuthPacket() : base(0x01)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var svc = stream.ReadByte();
            var dev = stream.ReadBoolean();
            var account = stream.ReadString(); // length 16, old variant
            var mac = stream.ReadBytes(8);
            var mac2 = stream.ReadBytes(8);
            var cpu = stream.ReadUInt64();
        }
    }
}