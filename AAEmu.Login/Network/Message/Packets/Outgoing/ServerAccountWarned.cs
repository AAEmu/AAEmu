using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Login.Network.Message.Packets.Outgoing
{
    [LoginMessage(LoginMessageOpcode.ServerAccountWarned)]
    public class ServerAccountWarned : IWritable
    {
        public byte Source { get; set; }
        public string Message { get; set; }

        public void Write(PacketStream stream)
        {
            stream.Write(Source);
            stream.Write(Message);
        }
    }
}
