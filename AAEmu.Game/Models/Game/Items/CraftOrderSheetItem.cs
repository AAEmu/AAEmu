using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Crafts;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

/// <summary>
/// A craft request sheet: which craft one character wants done, at what grade, how many times, and
/// which actability group that craft belongs to. Those four values are the item's own detail block.
/// </summary>
public class CraftOrderSheetItem : Item
{
    public override ItemDetailType DetailType => ItemDetailType.CraftOrderSheet;
    public override uint DetailBytesLength => CraftOrderSheetRules.DetailBytes;

    /// <summary>Craft the sheet stands for.</summary>
    public uint CraftId { get; set; }

    /// <summary>Product grade the order asks for. Zero when the craft does not pick a grade.</summary>
    public byte CraftGrade { get; set; }

    /// <summary>How many times the craft is asked for.</summary>
    public uint CraftCount { get; set; }

    /// <summary>Actability group of the craft, so the tooltip can name the proficiency.</summary>
    public uint ActabilityGroupId { get; set; }

    public CraftOrderSheetItem()
    {
    }

    public CraftOrderSheetItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
    }

    /// <summary>Writes the four values the sheet stands for.</summary>
    public void SetOrder(uint craftId, byte grade, uint count, uint actabilityGroupId)
    {
        CraftId = craftId;
        CraftGrade = grade;
        CraftCount = count;
        ActabilityGroupId = actabilityGroupId;
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;

        CraftId = stream.ReadUInt32();
        CraftGrade = stream.ReadByte();
        CraftCount = stream.ReadUInt32();
        ActabilityGroupId = stream.ReadUInt32();
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(CraftId);
        stream.Write(CraftGrade);
        stream.Write(CraftCount);
        stream.Write(ActabilityGroupId);
    }
}
