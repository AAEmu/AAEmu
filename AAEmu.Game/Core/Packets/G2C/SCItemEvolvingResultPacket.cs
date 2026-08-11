using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.Game.Core.Packets.G2C;

/// <summary>
/// Outcome of one synthesis ("Item Growth" / 합성) attempt, opcode 0xCD.
/// </summary>
/// <remarks>
/// <para>Schema:</para>
/// <code>
/// u64 itemId
/// u8  newGrade
/// u8  oldGrade
/// u8  addCount     // number of attribute entries that follow
/// u32 addExp
/// u32 bonusExp
/// u32 addChance
/// addCount x { u16 attribute, u8 modifierType, u32 value }
/// </code>
/// <para>
/// Order matters and is easy to get backwards: the <b>new</b> grade precedes the old one. The receiver
/// takes the first as the item's resulting grade and presents the pair as "current" then "new", so
/// swapping them both mis-grades the item and inverts the dialog.
/// </para>
/// <para>
/// <c>addCount</c> must match the number of entries written; it is the only length the reader has.
/// Sending equal grades together with <c>bonusExp</c> of 0 is the defined quiet outcome - no result
/// window - which is how an attempt that only added experience reports itself. Grades are sent even
/// when unchanged; the packet is the record of the attempt, not only of a change.
/// </para>
/// </remarks>
public class SCItemEvolvingResultPacket : GamePacket
{
    /// <summary>One rolled random attribute, as the client's per-entry reader expects it.</summary>
    public readonly record struct EvolvingAttribute(ushort Attribute, byte Type, int Value);

    private readonly ulong _itemId;
    private readonly byte _newGrade;
    private readonly byte _oldGrade;
    private readonly int _addExp;
    private readonly int _bonusExp;
    private readonly int _addChance;
    private readonly IReadOnlyList<EvolvingAttribute> _attributes;

    public SCItemEvolvingResultPacket(Item item, byte newGrade, byte oldGrade, int addExp, int bonusExp,
        int addChance, IReadOnlyList<EvolvingAttribute> attributes = null)
        : base(SCOffsets.SCItemEvolvingResultPacket, 1)
    {
        _itemId = item?.Id ?? 0;
        _newGrade = newGrade;
        _oldGrade = oldGrade;
        _addExp = addExp;
        _bonusExp = bonusExp;
        _addChance = addChance;
        _attributes = attributes ?? [];
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(_itemId);
        stream.Write(_newGrade);
        stream.Write(_oldGrade);
        stream.Write((byte)_attributes.Count);
        stream.Write(_addExp);
        stream.Write(_bonusExp);
        stream.Write(_addChance);
        foreach (var attribute in _attributes)
        {
            stream.Write(attribute.Attribute);
            stream.Write(attribute.Type);
            stream.Write(attribute.Value);
        }

        return stream;
    }
}
