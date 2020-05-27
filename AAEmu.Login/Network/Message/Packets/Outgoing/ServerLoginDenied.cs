using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;
using AAEmu.Login.Network.Message.Static;

namespace AAEmu.Login.Network.Message.Packets.Outgoing
{
    [LoginMessage(LoginMessageOpcode.ServerLoginDenied)]
    public class ServerLoginDenied : IWritable
    {
        public LoginResponse Reason { get; set; }
        public string Vp { get; set; }
        public string Message { get; set; }

        public void Write(PacketStream stream)
        {
            stream.Write((byte)Reason);
            stream.Write(Vp);
            stream.Write(Message);
        }
    }
}
