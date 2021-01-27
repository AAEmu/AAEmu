using System.Collections.Generic;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C
{
    public class SCTradeMadePacket : GamePacket
    {
        private readonly ItemTaskType _action;
        private readonly List<ItemTask> _tasks;
        private readonly List<ulong> _forceRemove;

        public SCTradeMadePacket(ItemTaskType action, List<ItemTask> tasks, List<ulong> forceRemove) : base(SCOffsets.SCTradeMadePacket, 5)
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
            return stream;
        }
    }
}
