namespace AAEmu.Commons.Network.Core.Packet
{
    public abstract class GamePacket<TOpcode>
    {
        public uint Size { get; protected set; }
        public TOpcode Opcode { get; set; }
        public PacketStream Data { get; protected set; }
        public byte Level { get; set; }
    }
}
