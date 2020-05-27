using AAEmu.Commons.Network.Core;

namespace AAEmu.Login.Network.Message.Packets.Incoming
{
    [LoginMessage(LoginMessageOpcode.ClientCancelEnterWorld)]
    public class ClientCancelEnterWorld : LoginReadable
    {
        public byte GameServerId { get; private set; }

        public override void Read(PacketStream stream)
        {
            GameServerId = stream.ReadByte();
        }
    }
}
