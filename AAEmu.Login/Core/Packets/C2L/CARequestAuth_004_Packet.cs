using System.Xml.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
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
            var mac = stream.ReadBytes();
            var param = stream.ReadString();     // param
            var signature = stream.ReadString(); // si

            var xmlDoc = XDocument.Parse(param);

            if (xmlDoc.Root == null)
            {
                _log.Error("RequestAuthTrion: Catch parse ticket");
                return;
            }

            var username = xmlDoc.Root.Element("username")?.Value;
            var password = xmlDoc.Root.Element("password")?.Value;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                _log.Error("RequestAuthTrion: username or password is empty or white space");
                return;
            }

            var token = Helpers.StringToByteArray(password);

            LoginController.Login(Connection, username, token);
        }
    }
}
