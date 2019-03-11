using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemUnk18 : ItemTask
    {
        private readonly byte _change;

        public ItemUnk18(byte change)
        {
            _change = change;
            _type = 0x12; // 18
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(_change);
            return stream;
        }
    }
}
