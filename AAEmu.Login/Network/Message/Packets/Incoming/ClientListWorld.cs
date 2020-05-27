using AAEmu.Commons.Network.Core;

namespace AAEmu.Login.Network.Message.Packets.Incoming
{
    [LoginMessage(LoginMessageOpcode.ClientListWorld)]
    public class ClientListWorld : LoginReadable
    {
        public ulong Flag { get; private set; }

        public override void Read(PacketStream stream)
        {
            Flag = stream.ReadUInt64();
        }
    }
}
