using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthMailRuPacket : LoginPacket
    {
        public CARequestAuthMailRuPacket() : base(CLOffsets.CARequestAuthMailRuPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var dev = stream.ReadBoolean();
            var mac = stream.ReadBytes();
            var id = stream.ReadString();
            var token = stream.ReadBytes();

            LoginController.Login(Connection, id, token);
        }
    }
}
