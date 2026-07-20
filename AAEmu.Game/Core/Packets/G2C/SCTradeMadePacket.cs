using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items.Actions;

namespace AAEmu.Game.Core.Packets.G2C;

public class SCTradeMadePacket(ItemTaskType action, List<ItemTask> tasks, List<ulong> forceRemove)
    : GamePacket(SCOffsets.SCTradeMadePacket, 5)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((byte)action);

        stream.Write((byte)tasks.Count); // TODO max count 30
        foreach (var task in tasks)
            stream.Write(task);

        stream.Write((byte)forceRemove.Count); // TODO max count 30
        foreach (var remove in forceRemove)
            stream.Write(remove);

        stream.Write(0u); // type(id)
        return stream;
    }
}
