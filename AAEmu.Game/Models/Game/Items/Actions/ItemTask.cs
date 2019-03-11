using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public abstract class ItemTask : PacketMarshaler
    {
        protected byte _type;

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(_type);
            return stream;
        }
    }
}
