using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Login.Network.Message.Packets.Outgoing
{
    [LoginMessage(LoginMessageOpcode.ServerEnterWorldDenied)]
    public class ServerEnterWorldDenied : IWritable
    {
        public byte Reason { get; set; }
        
        public void Write(PacketStream stream)
        {
            stream.Write(Reason);
        }
    }
}
