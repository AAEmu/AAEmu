using AAEmu.Commons.Network.Core.Messages;

namespace AAEmu.Commons.Network.Core.Packet
{
    public class ServerGamePacket<TOpcode> : GamePacket<TOpcode>
    {
        public ServerGamePacket(TOpcode opcode, IWritable message, byte level = 0)
        {
            var stream = new PacketStream();
            message.Write(stream);
            Data = stream;
            Opcode = opcode;
            Level = level;
        }
    }
}
