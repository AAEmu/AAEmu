using AAEmu.Commons.Network;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthTrionPacket : LoginPacket
    {
        public CARequestAuthTrionPacket() : base(0x04)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var dev = stream.ReadBoolean();
            var mac = stream.ReadBytes();
            var ticket = stream.ReadString();
            var signature = stream.ReadString();
            var isLast = stream.ReadBoolean();
            
            _log.Warn("RequestAuthTrion");
        }
    }
}
