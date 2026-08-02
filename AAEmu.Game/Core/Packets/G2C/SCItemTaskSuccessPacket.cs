using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC_PACKET_ITEM_TASK_SUCCESS (0x0BC).
/// trailing type s64, lockItemSlotKey s32, queryResult bool.
/// </summary>
public class SCItemTaskSuccessPacket : GamePacket
{
    private readonly ItemTaskType _action;
    private readonly List<ItemTask> _tasks;
    private readonly List<ulong> _forceRemove;
    private readonly byte _unitOwnerType;

    public SCItemTaskSuccessPacket(ItemTaskType action, List<ItemTask> tasks, List<ulong> forceRemove, byte unitOwnerType = 0)
        : base(SCOffsets.SCItemTaskSuccessPacket, 1)
    {
        _action = action;
        _tasks = tasks;
        _forceRemove = forceRemove;
        _unitOwnerType = unitOwnerType;
    }

    public SCItemTaskSuccessPacket(ItemTaskType action, ItemTask task, List<ulong> forceRemove, byte unitOwnerType = 0)
        : base(SCOffsets.SCItemTaskSuccessPacket, 1)
    {
        _action = action;
        _tasks = [task];
        _forceRemove = forceRemove;
        _unitOwnerType = unitOwnerType;
    }

    public override PacketStream Write(PacketStream stream)
    {
        var forceRemove = _forceRemove ?? [];
        ItemTaskListLimits.Validate(_tasks, forceRemove);

        stream.Write(_unitOwnerType);

        stream.Write((byte)_action);

        stream.Write((byte)_tasks.Count);
        foreach (var task in _tasks)
            stream.Write(task);

        stream.Write((byte)forceRemove.Count);
        foreach (var remove in forceRemove)
            stream.Write(remove);

        stream.Write(0L);       // trailing type (s64)
        stream.Write(0);        // lockItemSlotKey (s32)
        stream.Write(false);    // queryResult
        stream.Write(0UL);      // flags (u64 bitmask)
        return stream;
    }
}
