using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// One equip slot's reinforcement level and the experience banked towards the next one. This is what
/// fills the client's per-slot state, so it is sent for every slot of a ladder when a character enters
/// the world and again after each change.
/// </summary>
/// <remarks>
/// Field order and widths are the client's own: the object id, the slot, the level and the exp.
/// </remarks>
public class SCEquipSlotReinforceUpdatePacket(uint bc, byte equipSlot, sbyte level, int exp)
    : GamePacket(SCOffsets.SCEquipSlotReinforceUpdatePacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(bc);
        stream.Write(equipSlot);
        stream.Write(level);
        stream.Write(exp);
        return stream;
    }
}
