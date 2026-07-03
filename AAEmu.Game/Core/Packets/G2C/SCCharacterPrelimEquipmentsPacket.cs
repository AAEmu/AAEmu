using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

// SC_PACKET_CHARACTER_PRELIM_EQUIPMENTS (121). Body per x2game-dev_dedicate SCCharacterPrelimEquipmentsPacket
// serialize (sub_39B18A80): size(u32) followed by, per entry, EquipSlot(u8) + Item EquipView. The reference sends
// this at world entry right after the inventory contents; without it the client's equipment view stays
// uninitialized. The full equipped set is already carried by SCCharacterState (WriteLobby1013), so the prelim list
// is emitted empty to initialize the client structure ahead of the player-frame render.
public class SCCharacterPrelimEquipmentsPacket() : GamePacket(SCOffsets.SCCharacterPrelimEquipmentsPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(0u); // size (prelim equipment entry count)
        return stream;
    }
}
