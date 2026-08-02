using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC 0x296 — push live hull HP onto the summon scroll in the owner's inventory.
/// and patches the scroll so GetMySlaveHealth reads the current HP. Retail sends this whenever
/// the bound hull's HP should be reflected on its source item (spawn + periodic MySlave).
/// EquipSlot 0xFF = "resolve by itemId" (handler treats -1 as id lookup).
/// </summary>
public class SCUpdatedSlaveSourceItemPacket(
    uint ownerUnitId,
    ulong itemId,
    int health,
    byte equipSlot = 0xFF)
    : GamePacket(SCOffsets.SCUpdatedSlaveSourceItemPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(ownerUnitId);
        stream.Write(itemId);
        stream.Write(health);
        stream.Write(equipSlot);
        return stream;
    }
}
