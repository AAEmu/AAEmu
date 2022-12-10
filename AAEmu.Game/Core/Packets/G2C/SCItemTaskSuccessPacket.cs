using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCItemTaskSuccessPacket : GamePacket
    {
        private readonly ItemTaskType _action;
        private readonly List<ItemTask> _tasks;
        private readonly List<ulong> _forceRemove;

        public SCItemTaskSuccessPacket(ItemTaskType action, List<ItemTask> tasks, List<ulong> forceRemove) : base(SCOffsets.SCItemTaskSuccessPacket, 1)
        {
            _action = action;
            _tasks = tasks;
            _forceRemove = forceRemove;
        }

        public SCItemTaskSuccessPacket(ItemTaskType action, ItemTask task, List<ulong> forceRemove) : base(SCOffsets.SCItemTaskSuccessPacket, 1)
        {
            _action = action;
            _tasks = new List<ItemTask>() { task };
            _forceRemove = forceRemove;
        }
        
        public override PacketStream Write(PacketStream stream)
        {
            stream.Write((byte) _action);

            stream.Write((byte) _tasks.Count); // TODO max count 30
            foreach (var task in _tasks)
                stream.Write(task);

            if (_forceRemove == null)
            {
                stream.Write((byte)0);
            }
            else
            {
                stream.Write((byte)_forceRemove.Count); // TODO max count 30
                foreach (var remove in _forceRemove)
                    stream.Write(remove);
            }

            stream.Write(0u); // type(id)
            return stream;
        }
    }
}
