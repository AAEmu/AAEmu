using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Clears an equip slot's level effect, which happens when the slot moves to an effect that replaces
/// it or when the level it was granted at is lost.
/// </summary>
/// <remarks>
/// Field order and widths are the client's own: the object id, the slot and the level.
/// </remarks>
public class SCEquipSlotReinforceLevelEffectDeletePacket(uint bc, byte equipSlot, sbyte level)
    : GamePacket(SCOffsets.SCEquipSlotReinforceLevelEffectDeletePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(equipSlot);
        stream.Write(level);
        return stream;
    }
}
