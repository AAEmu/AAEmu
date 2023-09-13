using System.Xml.Linq;
using AAEmu.Commons.Network;
using AAEmu.Commons.Utils;
using AAEmu.Login.Core.Controllers;
using AAEmu.Login.Core.Network.Login;

namespace AAEmu.Login.Core.Packets.C2L
{
    public class CARequestAuthTrionPacket : LoginPacket
    {
        public CARequestAuthTrionPacket() : base(CLOffsets.CARequestAuthTrionPacket)
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

            var xmlDoc = XDocument.Parse(ticket);

            if (xmlDoc.Root == null)
            {
                _log.Error("RequestAuthTrion: Catch parse ticket");
                return;
            }

            var username = xmlDoc.Root.Element("username")?.Value;
            var password = xmlDoc.Root.Element("password")?.Value;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                _log.Error("RequestAuthTrion: username or password is empty or whitespace");
                return;
            }

            var token = Helpers.StringToByteArray(password);
            LoginController.Login(Connection, username, token);
        }
    }
}
