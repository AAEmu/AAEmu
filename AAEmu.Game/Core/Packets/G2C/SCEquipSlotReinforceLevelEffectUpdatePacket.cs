using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Tells the client which level effect an equip slot is now running, so it can mark the choice in the
/// slot's window and show the modifiers that go with it.
/// </summary>
/// <remarks>
/// Field order and widths are the client's own: the object id, the slot, the level and the effect.
/// </remarks>
public class SCEquipSlotReinforceLevelEffectUpdatePacket(uint bc, byte equipSlot, sbyte level, uint levelEffectId)
    : GamePacket(SCOffsets.SCEquipSlotReinforceLevelEffectUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(equipSlot);
        stream.Write(level);
        stream.Write(levelEffectId);
        return stream;
    }
}
