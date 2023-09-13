using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game
{
    public class Blocked : PacketMarshaler
    {
        public uint CharacterId { get; set; }
        public string Name { get; set; }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(CharacterId);
            stream.Write(Name);
            return stream;
        }
    }
}
