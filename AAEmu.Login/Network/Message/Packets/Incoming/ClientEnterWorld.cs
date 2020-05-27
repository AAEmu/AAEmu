using AAEmu.Commons.Network.Core;

namespace AAEmu.Login.Network.Message.Packets.Incoming
{
    [LoginMessage(LoginMessageOpcode.ClientEnterWorld)]
    public class ClientEnterWorld : LoginReadable
    {
        public ulong Flag { get; private set; }
        public byte GameServerId { get; private set; }

        public override void Read(PacketStream stream)
        {
            Flag = stream.ReadUInt64();
            GameServerId = stream.ReadByte();
        }
    }
}
