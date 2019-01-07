using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;
using AAEmu.Login.Core.Packets.L2C;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthMailRuPacket : LoginPacket
    {
        public CARequestAuthMailRuPacket() : base(0x04)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var dev = stream.ReadBoolean();

            var macLength = stream.ReadUInt16(); // TODO or nope?
            var mac = stream.ReadBytes(macLength);

            var id = stream.ReadString(); // length 31

            var tokenLength = stream.ReadUInt16();
            var token = stream.ReadBytes(tokenLength);

            LoginController.Login(Connection, id, token);
        }
    }
}