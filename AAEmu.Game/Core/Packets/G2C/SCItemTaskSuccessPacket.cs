using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCItemTaskSuccessPacket : GamePacket
    {
        private readonly ItemTaskType _action;
        private readonly List<ItemTask> _tasks;
        private readonly List<ulong> _forceRemove;

        public SCItemTaskSuccessPacket(ItemTaskType action, List<ItemTask> tasks, List<ulong> forceRemove) : base(SCOffsets.SCItemTaskSuccessPacket, 5)
        {
            _action = action;
            _tasks = tasks;
            _forceRemove = forceRemove;
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) _action);

            stream.Write((byte) _tasks.Count); // TODO max count 30
            foreach (var task in _tasks)
                stream.Write(task);

            stream.Write((byte) _forceRemove.Count); // TODO max count 30
            foreach (var remove in _forceRemove)
                stream.Write(remove);

            stream.Write(0u); // type(id)

            var index = 0;
            var ItemFlags = 0u;
            var items = Connection.ActiveChar.Inventory.Equipment.GetSlottedItemsList();
            foreach (var item in items)
            {
                if (item != null)
                {
                    var v15 = (uint)item.ItemFlags << index;
                    ++index;
                    ItemFlags |= v15;
                }
            }
            stream.Write(ItemFlags); //  flags

            return stream;
        }
    }
}
