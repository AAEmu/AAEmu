using AAEmu.Commons.Network.Core;

namespace AAEmu.Login.Network.Message.Packets.Incoming
{
    [LoginMessage(LoginMessageOpcode.ClientRequestReconnect)]
    public class ClientRequestReconnect : LoginReadable
    {
        public uint From { get; private set; }
        public uint To { get; private set; }
        public ulong AccountId { get; private set; }
        public byte GameServerId { get; private set; } // wid
        public int Token { get; private set; } // cookie
        public byte[] Mac { get; private set; }
        
        public override void Read(PacketStream stream)
        {
            From = stream.ReadUInt32();
            To = stream.ReadUInt32();
            AccountId = stream.ReadUInt64();
            GameServerId = stream.ReadByte();
            Token = stream.ReadInt32();
            Mac = stream.ReadBytes();
        }
    }
}
