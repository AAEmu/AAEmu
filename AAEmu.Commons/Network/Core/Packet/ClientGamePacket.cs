namespace AAEmu.Commons.Network.Core.Packet
{
    public class ClientGamePacket<TOpcode> : GamePacket<TOpcode>
    {
        public byte Level { get; set; }
        
        public ClientGamePacket(TOpcode opcode, PacketStream data)
        {
            Opcode = opcode;
            Data = data;
        }
        
        public ClientGamePacket(TOpcode opcode, byte level, PacketStream data)
        {
            Opcode = opcode;
            Level = level;
            Data = data;
        }
    }
}
