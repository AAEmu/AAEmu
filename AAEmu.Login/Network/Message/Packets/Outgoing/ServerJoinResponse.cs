using AAEmu.Commons.Network.Core;
using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Login.Network.Message.Packets.Outgoing
{
    [LoginMessage(LoginMessageOpcode.ServerJoinResponse)]
    public class ServerJoinResponse : IWritable
    {
        public ushort Reason { get; set; }
        public ulong Afs { get; set; }

        public void Write(PacketStream stream)
        {
            stream.Write(Reason);
            stream.Write(Afs);

            // afs[0] -> max number of characters per account
            // afs[1] -> additional number of characters per server when using the item to increase the slot
            // afs[2] -> 1 - character pre-creation mode
        }
    }
}
