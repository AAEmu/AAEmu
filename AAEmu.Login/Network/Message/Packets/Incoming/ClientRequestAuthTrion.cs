using AAEmu.Commons.Network.Core;

namespace AAEmu.Login.Network.Message.Packets.Incoming
{
    [LoginMessage(LoginMessageOpcode.ClientRequestAuthTrion)]
    public class ClientRequestAuthTrion : LoginReadable
    {
        public uint From { get; private set; }
        public uint To { get; private set; }
        public byte Svc { get; private set; }
        public bool Dev { get; private set; }
        public byte[] Mac { get; private set; }
        public string Ticket { get; private set; }

        public override void Read(PacketStream stream)
        {
            From = stream.ReadUInt32();
            To = stream.ReadUInt32();
            Svc = stream.ReadByte();
            Dev = stream.ReadBoolean();
            Ticket = stream.ReadString(); // account
            Mac = stream.ReadBytes();
            var mac2 = stream.ReadBytes();
            var cpu = stream.ReadUInt64();
        }
    }
}
