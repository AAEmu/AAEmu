using System.Collections.Generic;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCItemTaskSuccessPacket : GamePacket
{
    private readonly ItemTaskType _action;
    private readonly List<ItemTask> _tasks;
    private readonly List<ulong> _forceRemove;

    public SCItemTaskSuccessPacket(ItemTaskType action, List<ItemTask> tasks, List<ulong> forceRemove)
        : base(SCOffsets.SCItemTaskSuccessPacket, 5)
    {
        _action = action;
        _tasks = tasks;
        _forceRemove = forceRemove;
    }

    public SCItemTaskSuccessPacket(ItemTaskType action, ItemTask task, List<ulong> forceRemove) : base(SCOffsets.SCItemTaskSuccessPacket, 5)
    {
        _action = action;
        _tasks = [task];
        _forceRemove = forceRemove;
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)_action);

        stream.Write((byte)_tasks.Count); // TODO max count 30
        foreach (var task in _tasks)
            stream.Write(task);

        stream.Write((byte)_forceRemove.Count); // TODO max count 30
        foreach (var remove in _forceRemove)
            stream.Write(remove);

        stream.Write(0u); // type(id)
        stream.Write(0u); // lockItemSlotKey
        stream.Write(0u); // flags

        return stream;
    }

    //public void ProcessFlags(PacketStream stream)
    //{
    //    uint flags = 0;
    //    uint v8;

    //    for (var j = 0; j < 31; ++j)
    //    {
    //        v8 = this[j + 19480] << j;
    //        flags |= v8;
    //    }

    //    uint v9 = flags;
    //    stream.Write(0u); // flags
    //}
}
