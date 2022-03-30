using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthKakaoPacket : LoginPacket
    {
        public CARequestAuthKakaoPacket() : base(CLOffsets.CARequestAuthKakaoPacket)
        {
        }

        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var svc = stream.ReadByte();
            var dev = stream.ReadBoolean();
            var mac = stream.ReadString(); // 8
            var mac2 = stream.ReadString(); // 8
            var accessToken = stream.ReadString(); // 2047
            var is64bit = stream.ReadBoolean();

            //var xmlDoc = XDocument.Parse(ticket);

            //if (xmlDoc.Root == null)
            //{
            //    _log.Error("RequestAuthTrion: Catch parse ticket");
            //    return;
            //}

            //var username = xmlDoc.Root.Element("username")?.Value;
            //var password = xmlDoc.Root.Element("password")?.Value;

            //if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            //{
            //    _log.Error("RequestAuthTrion: username or password is empty or white space");
            //    return;
            //}

            var username = "test"; // только для теста
            var password = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

            var token = Helpers.StringToByteArray(password);
            LoginController.Login(Connection, username, token);
        }
    }
}
