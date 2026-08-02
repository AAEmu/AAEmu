using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// SC_PACKET_CHARACTER_PRELIM_EQUIPMENTS (0x079).
///   u32 size; then size × { s8 EquipSlot, EquipView }.
/// CN sniff body <c>020000000f000000001000000000</c> = size=2, slots 15/16 with empty type=0.
/// Emitting size=0 leaves the client's preliminary_equipments.lua <c>buttonStatus</c> nil and
/// breaks the C (character info) window while inventory (I) still works.
/// </summary>
public class SCCharacterPrelimEquipmentsPacket() : GamePacket(SCOffsets.SCCharacterPrelimEquipmentsPacket, 1)
{
    // Slots observed on live CN enter-world (empty prelim set).
    private static readonly sbyte[] DefaultEmptySlots = [0x0F, 0x10];

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write((uint)DefaultEmptySlots.Length);
        foreach (var slot in DefaultEmptySlots)
        {
            stream.Write(slot); // EquipSlot s8
            stream.Write(0u);   // EquipView type = empty sentinel (dword_3A6ABA14 == 0)
        }

        return stream;
    }
}
