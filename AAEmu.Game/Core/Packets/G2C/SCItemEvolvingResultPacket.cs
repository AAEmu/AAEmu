using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Outcome of one synthesis attempt.
/// </summary>
/// <remarks>
/// <para>Wire shape:</para>
/// <code>
/// u64 itemId
/// u8  newGrade      // the grade the item ends the attempt at
/// u8  oldGrade      // the grade it started at
/// u8  addCount      // number of attribute entries that follow
/// u32 addExp
/// u32 bonusExp
/// u32 addChance
/// addCount x { u16 attribute, u8 modifierType, u32 value }
/// </code>
/// <para>
/// The two grades are easy to get backwards and the <b>new</b> one comes first. The client takes the
/// pair as the record of the attempt and drives its result window off it, so a swap does not merely
/// mislabel the dialog - it reads as a downgrade and the window does not play out.
/// </para>
/// <para>
/// Both grades are sent even when nothing changed; equal grades with a <c>bonusExp</c> of 0 is the
/// defined quiet outcome, which is how an attempt that only banked experience reports itself.
/// <c>addCount</c> is the only length the reader has for the trailing run, so it has to match.
/// </para>
/// </remarks>
public class SCItemEvolvingResultPacket(
    ulong itemId,
    byte newGrade,
    byte oldGrade,
    uint addExp,
    uint bonusExp,
    uint addChance,
    List<ItemRndAttrUnitModifier> addedAttributes)
    : GamePacket(SCOffsets.SCItemEvolvingResultPacket, 1)
{
    public override PacketStream Write(PacketStream stream)
    {
        var attributes = addedAttributes ?? [];

        stream.Write(itemId);
        stream.Write(newGrade);
        stream.Write(oldGrade);
        stream.Write((byte)attributes.Count);
        stream.Write(addExp);
        stream.Write(bonusExp);
        stream.Write(addChance);

        foreach (var attribute in attributes)
        {
            stream.Write(attribute.Attribute);
            stream.Write(attribute.ModifierType);
            stream.Write(attribute.Value);
        }

        return stream;
    }
}
