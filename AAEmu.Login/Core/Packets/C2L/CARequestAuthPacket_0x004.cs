using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthPacket_0x004 : LoginPacket
    {
        public CARequestAuthPacket_0x004() : base(CLOffsets.CARequestAuthPacket_0x004)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var dev = stream.ReadBoolean();
            var mac = stream.ReadBytes();    // 8
            var param = stream.ReadString(); // 1023
            var si = stream.ReadString();    // 15
            var is64bit = stream.ReadBoolean();    // added 5.7.5.0

            //LoginController.Login(Connection, id, tkn);
        }
    }
}
