using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// (maximum 30), ids i64, trailing type i64, lock-item-slot key i32, query-result bool.
/// </summary>
public class SCTradeMadePacket(ItemTaskType action, List<ItemTask> tasks, List<ulong> forceRemove)
    : GamePacket(SCOffsets.SCTradeMadePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        ItemTaskListLimits.Validate(tasks, forceRemove);

        stream.Write((byte)action);

        stream.Write((byte)tasks.Count);
        foreach (var task in tasks)
            stream.Write(task);

        stream.Write((byte)forceRemove.Count);
        foreach (var remove in forceRemove)
            stream.Write(remove);

        stream.Write(0L);    // trailing type (i64)
        stream.Write(0);     // lockItemSlotKey (i32)
        stream.Write(false); // queryResult
        return stream;
    }
}
