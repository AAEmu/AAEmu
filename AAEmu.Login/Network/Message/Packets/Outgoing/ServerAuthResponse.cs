using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Login.Network.Message.Packets.Outgoing
{
    [LoginMessage(LoginMessageOpcode.ServerAuthResponse)]
    public class ServerAuthResponse : IWritable
    {
        public ulong AccountId { get; set; }
        public string WebSessionKey { get; set; }
        public byte SlotCount { get; set; }

        public void Write(PacketStream stream)
        {
            stream.Write(AccountId);
            stream.Write(WebSessionKey);
            stream.Write(SlotCount);
        }
    }
}
