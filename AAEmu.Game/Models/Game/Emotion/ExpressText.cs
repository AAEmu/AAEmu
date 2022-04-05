using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Emotion
{
    public class ExpressText : PacketMarshaler
    {
        public uint Id { get; set; }
        public uint AnimId { get; set; }
    }
}
