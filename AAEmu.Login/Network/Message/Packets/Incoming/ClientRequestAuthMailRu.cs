using AAEmu.Commons.Network.Core;

namespace AAEmu.Login.Network.Message.Packets.Incoming
{
    [LoginMessage(LoginMessageOpcode.ClientRequestAuthMailRu)]
    public class ClientRequestAuthMailRu : LoginReadable
    {
        public override void Read(PacketStream stream)
        {
            var pFrom = stream.ReadUInt32();
            var pTo = stream.ReadUInt32();
            var dev = stream.ReadBoolean();
            var mac = stream.ReadBytes();
            var id = stream.ReadString();
            var token = stream.ReadBytes();
        }
    }
}
