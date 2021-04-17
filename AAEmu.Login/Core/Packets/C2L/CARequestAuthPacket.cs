using AAEmu.Commons.Network;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthPacket : LoginPacket
    {
        public CARequestAuthPacket() : base(CLOffsets.CARequestAuthPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var svc = stream.ReadByte();
            var dev = stream.ReadBoolean();
            var account = stream.ReadString();
            var mac = stream.ReadBytes();
            var mac2 = stream.ReadBytes();
            var cpu = stream.ReadUInt64();

            LoginController.Login(Connection, account);

            // Connection.SendPacket(new ACChallengePacket()); // TODO ...
        }
    }
}
