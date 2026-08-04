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
    // 18-value gem/socket block carried by the pish/pisc codec. The per-value semantics (which entries are
    // GemIds/Temper) still need RE of the in-memory gem struct; meanwhile this round-trips byte-correct.
    public uint[] GemData { get; set; }

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
        GemData = new uint[18];
    }

    public EquipItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
        GemIds = new uint[7];
        GemData = new uint[18];
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

    // 18-value pish/pisc gem block. Variable length — replaces the v1.2 fixed 55-byte blob. NOTE: this is
    // the same serializer used to persist the items.details blob, so the DB detail format is now v10.
    public override void ReadDetails(PacketStream stream)
    {
        Durability = stream.ReadByte();
        ChargeCount = stream.ReadUInt16(); // chargeCount is u16 (binary serializer vtbl+168, 2 bytes), not i32
        ChargeStartTime = stream.ReadDateTime();
        RuneId = stream.ReadUInt16();
        EvolveChance = stream.ReadUInt16();
        ChargeProcTime = stream.ReadDateTime();
        MappingFailBonus = stream.ReadByte();
        ElementLevel = stream.ReadByte();
        GemData = stream.ReadPisc(18);
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(Durability);          // durability u8
        stream.Write((ushort)ChargeCount); // chargeCount u16 (binary serializer vtbl+168, 2 bytes)
        stream.Write(ChargeStartTime);     // chargeTime i64
        stream.Write((ushort)RuneId);      // runeId u16
        stream.Write(EvolveChance);        // evolveChance u16
        stream.Write(ChargeProcTime);      // chargeProcTime i64
        stream.Write(MappingFailBonus);    // mappingFailBonus u8
        stream.Write(ElementLevel);        // elementLevel u8
        // then 14 gem ints. ImageItemTemplateId must occupy GemData[0] on the wire.
        var gemData = GemData ?? new uint[18];
        if (gemData.Length < 18)
            Array.Resize(ref gemData, 18);
        gemData[0] = ImageItemTemplateId;
        stream.WritePisc(gemData);
    }
}
