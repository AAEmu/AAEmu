using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Says which equipment slots have their synthesis effects counting toward the wearer's attributes.
/// </summary>
/// <remarks>
/// <para>
/// One bit per slot, in the same 34-slot order the equipment block uses. The client keeps a byte per
/// slot and asks it last, after it has established the piece has a synthesis pool at all; a slot
/// whose bit is clear contributes its own stats, its rune and its lunagems and nothing else.
/// </para>
/// <para>
/// The mask otherwise rides along only with the unit state, which is not sent again for a change
/// made to a piece already being worn - hence this packet. The client spells its own name
/// "Avtivate"; the typo is theirs and is not repeated here.
/// </para>
/// </remarks>
public sealed class SCUnitEquipmentsRndAttrUnitModifierActivatedPacket(Unit unit)
    : GamePacket(SCOffsets.SCUnitEquipmentsRndAttrUnitModifierActivatedPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        stream.WriteBc(unit.ObjId);
        stream.Write((long)EquipmentSerializer.BuildRndAttrActivationMask(unit));
        return stream;
    }
}
