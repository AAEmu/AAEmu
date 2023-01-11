using AAEmu.Commons.Network;

namespace AAEmu.Game.Models.Game.Items.Actions
{
    public class ItemRemoveCrafting : ItemTask
    {
        private readonly ulong _id;

        public ItemRemoveCrafting(ulong id)
        {
            _type = ItemAction.RemoveCrafting; // 12
            _id = id;
        }

        public override PacketStream Write(PacketStream stream)
        {
            base.Write(stream);
            stream.Write(_id); // id
            return stream;
        }
    }
}
