using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class EquipItem : Item
{
    public override ItemDetailType DetailType => ItemDetailType.Equipment;

    public byte Durability { get; set; }
    public uint RuneId { get; set; }
    public uint[] GemIds { get; set; }
    public ushort TemperPhysical { get; set; }
    public ushort TemperMagical { get; set; }

    public ushort EvolveChance { get; set; }
    public DateTime ChargeProcTime { get; set; } = DateTime.MinValue;
    public byte MappingFailBonus { get; set; }
    public byte ElementLevel { get; set; }
    /// <summary>
    /// The equipment detail's value block, serialized and persisted as a whole by the pish/pisc codec.
    /// </summary>
    /// <remarks>
    /// Its length and the position of every value are part of the item detail contract, shared by the
    /// network body and the stored <c>items.details</c> blob, so entries are addressed through the
    /// named indices below and the block is always <see cref="GemDataSlots"/> long. Values this server
    /// does not yet interpret still round-trip unchanged, which is what keeps a detail written by an
    /// older build readable. Assigning the array does not by itself mark the item dirty; the accessors
    /// below do that.
    /// </remarks>
    public uint[] GemData { get; set; }

    /// <summary>Length of <see cref="GemData"/>. Fixed by the item detail contract.</summary>
    public const int GemDataSlots = 18;

    /// <summary>
    /// Synthesis ("Item Growth") experience accumulated at the current grade.
    /// </summary>
    /// <remarks>
    /// Held in <see cref="GemData"/> at <see cref="EvolvingExpGemDataIndex"/>. Never negative: a lower
    /// value is clamped to zero rather than wrapping, since the block is unsigned. Writing it marks the
    /// item dirty, without which the value reaches the client and is then lost at the next persist pass.
    /// </remarks>
    public int EvolvingExp
    {
        get => (int)(GemData is { Length: > EvolvingExpGemDataIndex } ? GemData[EvolvingExpGemDataIndex] : 0u);
        set
        {
            var gemData = GemData ?? new uint[GemDataSlots];
            if (gemData.Length < GemDataSlots)
                Array.Resize(ref gemData, GemDataSlots);
            gemData[EvolvingExpGemDataIndex] = (uint)Math.Max(0, value);
            GemData = gemData;
            IsDirty = true;
        }
    }

    /// <summary>Index of the synthesis experience within <see cref="GemData"/>.</summary>
    private const int EvolvingExpGemDataIndex = 3;

    /// <summary>How many synthesis effects an item can carry.</summary>
    public const int RndAttrSlots = 5;

    /// <summary>Index of the first synthesis effect within <see cref="GemData"/>; they are contiguous.</summary>
    private const int RndAttrFirstGemDataIndex = 13;

    /// <summary>
    /// The "Synthesis Effect" lines this item carries, as
    /// <c>item_rnd_attr_unit_modifier_groups</c> ids.
    /// </summary>
    /// <remarks>
    /// Held in <see cref="GemData"/> at <see cref="RndAttrFirstGemDataIndex"/> and the
    /// <see cref="RndAttrSlots"/> - 1 entries after it. Only the group is stored, never a magnitude:
    /// the value of an effect is looked up from <c>item_rnd_attr_unit_modifiers</c> for that group at
    /// the item's current grade, which is why the same effect is worth more as the item is synthesized.
    /// Reading yields only the occupied slots; writing takes at most <see cref="RndAttrSlots"/> ids and
    /// zeroes the rest, so assigning an empty sequence clears them all.
    /// </remarks>
    public IEnumerable<uint> RndAttrGroupIds
    {
        get
        {
            for (var i = 0; i < RndAttrSlots; i++)
            {
                var id = GemData is { Length: >= GemDataSlots } ? GemData[RndAttrFirstGemDataIndex + i] : 0u;
                if (id != 0)
                    yield return id;
            }
        }
        set
        {
            var gemData = GemData ?? new uint[GemDataSlots];
            if (gemData.Length < GemDataSlots)
                Array.Resize(ref gemData, GemDataSlots);

            var ids = (value ?? []).Take(RndAttrSlots).ToArray();
            for (var i = 0; i < RndAttrSlots; i++)
                gemData[RndAttrFirstGemDataIndex + i] = i < ids.Length ? ids[i] : 0u;

            GemData = gemData;
            IsDirty = true;
        }
    }

    public virtual int Str => 0;
    public virtual int Dex => 0;
    public virtual int Sta => 0;
    public virtual int Int => 0;
    public virtual int Spi => 0;
    public virtual byte MaxDurability => 0;

    /// <summary>
    /// The item ID of the dye pot that was used on the equipment.
    /// </summary>
    public uint DyeItemId { get; set; }

    public int RepairCost
    {
        get
        {
            var template = (EquipItemTemplate)Template;
            var grade = ItemManager.Instance.GetGradeTemplate(Grade);
            var cost = ItemManager.Instance.GetDurabilityRepairCostFactor() * 0.0099999998f *
                       (1f - Durability * 1f / MaxDurability) * template.Price;
            cost = cost * grade.RefundMultiplier * 0.0099999998f;
            cost = (float)Math.Ceiling(cost);
            if (cost < 0 || cost < int.MinValue || cost > int.MaxValue)
                cost = 0;
            return (int)cost;
        }
    }

    public EquipItem()
    {
        GemIds = new uint[7];
        GemData = new uint[GemDataSlots];
    }

    public EquipItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
        GemIds = new uint[7];
        GemData = new uint[GemDataSlots];
        // 10.0.2.13: DefaultDyeItemId removed; DyeItemId defaults to 0 (was always 0 via mock)
    }

    public override void Read(PacketStream stream)
    {
        TemplateId = stream.ReadUInt32();

        if (TemplateId == 0)
            return;

        Id = stream.ReadUInt64();
        Grade = stream.ReadByte();
        ItemFlags = (ItemFlag)stream.ReadByte();
        Count = stream.ReadInt32();
        var detailType = stream.ReadByte();
        ReadDetails(stream);
        CreateTime = stream.ReadDateTime();
        LifespanMins = stream.ReadInt32();
        MadeUnitId = (uint)stream.ReadUInt64(); // v10: madeUnitId is 8 bytes on the wire
        WorldId = stream.ReadByte();
        UnsecureTime = stream.ReadDateTime();
        UnpackTime = stream.ReadDateTime();
        ChargeUseSkillTime = stream.ReadDateTime(); // v10: new trailing field
    }

    // The same body serves the network detail and the persisted items.details blob, so a change here
    // changes the stored format too. GemData is variable length on the wire; GemDataSlots is what this
    // server reads and writes.
    public override void ReadDetails(PacketStream stream)
    {
        Durability = stream.ReadByte();
        ChargeCount = stream.ReadUInt16(); // chargeCount is u16, not i32
        ChargeStartTime = stream.ReadDateTime();
        RuneId = stream.ReadUInt16();
        EvolveChance = stream.ReadUInt16();
        ChargeProcTime = stream.ReadDateTime();
        MappingFailBonus = stream.ReadByte();
        ElementLevel = stream.ReadByte();
        GemData = stream.ReadPisc(GemDataSlots);
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(Durability);          // durability u8
        stream.Write((ushort)ChargeCount); // chargeCount u16
        stream.Write(ChargeStartTime);     // chargeTime i64
        stream.Write((ushort)RuneId);      // runeId u16
        stream.Write(EvolveChance);        // evolveChance u16
        stream.Write(ChargeProcTime);      // chargeProcTime i64
        stream.Write(MappingFailBonus);    // mappingFailBonus u8
        stream.Write(ElementLevel);        // elementLevel u8
        // then 14 gem ints. ImageItemTemplateId must occupy GemData[0] on the wire.
        var gemData = GemData ?? new uint[GemDataSlots];
        if (gemData.Length < GemDataSlots)
            Array.Resize(ref gemData, GemDataSlots);
        gemData[0] = ImageItemTemplateId;
        stream.WritePisc(gemData);
    }
}
