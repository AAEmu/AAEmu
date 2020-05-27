using AAEmu.Commons.Network;
using AAEmu.Commons.Network.Core;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemUnk12 : ItemTask
    {
        private readonly ulong _id;

        public ItemUnk12(ulong id)
        {
            _id = id;
            _type = 0xC; // 12
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(_id);
            return stream;
        }
    }
}
