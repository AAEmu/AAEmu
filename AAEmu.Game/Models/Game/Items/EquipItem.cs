using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class EquipItem : Item
{
    public override ItemDetailType DetailType => ItemDetailType.Equipment;
    public override uint DetailBytesLength => 38; // 38 - 3.5.0.3, 35 - 3.0.3.0, 55 - 1.2

    public uint EfainCubeId { get; set; }
    public uint RemainingExperience { get; set; }
    public uint LunaStone { get; set; }

    /// <summary>
    /// Cache for pre-calculated evolved attribute values to avoid repeated calculations
    /// </summary>
    public Dictionary<int, int> EvolvedAttributeValues { get; set; }

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

    public bool IsNotDestroyed
    {
        get
        {
            return MaxDurability <= 0 || Durability > 0;
        }
    }

    public EquipItem()
    {
        GemIds = new uint[GemIdMaxCount]; // 18 + 4 = 22 in 3.5.0.3 & 5.0.7.0, 16 in 3.0.3.0, 7 in 1.2
        EvolvedAttributeValues = new Dictionary<int, int>();
    }

    public EquipItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
        GemIds = new uint[GemIdMaxCount]; // 18 + 4 = 22 in 3.5.0.3 & 5.0.7.0, 16 in 3.0.3.0, 7 in 1.2
        EvolvedAttributeValues = new Dictionary<int, int>();
        DyeItemId = ((EquipItemTemplate)Template).DefaultDyeItemId;
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
        MadeUnitId = stream.ReadUInt32();
        WorldId = stream.ReadByte();
        UnsecureTime = stream.ReadDateTime();
        UnpackTime = stream.ReadDateTime();
    }

    public override void ReadDetails(PacketStream stream)
    {
        if (stream.LeftBytes < DetailBytesLength)
            return;
        Durability = stream.ReadByte();       // durability
        ChargeCount = stream.ReadInt16();     // chargeCount
        ChargeTime = stream.ReadDateTime();   // chargeTime
        TemperPhysical = stream.ReadUInt16(); // scaledA
        TemperMagical = stream.ReadUInt16();  // scaledB

        var mGems = stream.ReadPisc(4);
        GemIds[0] = (uint)mGems[0];  // Can modify the appearance, TemplateId предмета для внешнего вида
        ImageItemTemplateId = (uint)mGems[0];
        GemIds[1] = (uint)mGems[1];  // Luna Stone
        LunaStone = GemIds[1];
        GemIds[2] = (uint)mGems[2];  // эффект эфенских кубов
        EfainCubeId = GemIds[2];
        GemIds[3] = (uint)mGems[3];  // Synthesis experience
        RemainingExperience = GemIds[3];

        mGems = stream.ReadPisc(4);
        GemIds[4] = (uint)mGems[0];  // 1 crescent stone
        GemIds[5] = (uint)mGems[1];  // 2 crescent stone
        GemIds[6] = (uint)mGems[2];  // 3 crescent stone
        GemIds[7] = (uint)mGems[3];  // 4 crescent stone

        mGems = stream.ReadPisc(4);
        GemIds[8] = (uint)mGems[0];  // 5 crescent stone
        GemIds[9] = (uint)mGems[1];  // 6 crescent stone
        GemIds[10] = (uint)mGems[2]; // 7 crescent stone
        GemIds[11] = (uint)mGems[3]; // 8 crescent stone

        mGems = stream.ReadPisc(4);
        GemIds[12] = (uint)mGems[0]; // 9 crescent stone
        GemIds[13] = (uint)mGems[1]; // 0 Additional Effects
        GemIds[14] = (uint)mGems[2]; // 1 Additional Effects
        GemIds[15] = (uint)mGems[3]; // 2 Additional Effects

        mGems = stream.ReadPisc(2);
        GemIds[16] = (uint)mGems[0]; // 3 Additional Effects
        GemIds[17] = (uint)mGems[1]; // 4 Additional Effects
    }

    public override void WriteDetails(PacketStream stream)
    {
        NormalizeSynthesisExperienceForSerialization();

        stream.Write(Durability);     // durability
        stream.Write(ChargeCount);    // chargeCount
        stream.Write(ChargeTime);     // chargeTime
        stream.Write(TemperPhysical); // scaledA
        stream.Write(TemperMagical);  // scaledB

        GemIds[0] = ImageItemTemplateId;
        GemIds[1] = LunaStone;
        GemIds[2] = EfainCubeId;
        GemIds[3] = RemainingExperience;

        stream.WritePisc(GemIds[0], GemIds[1], GemIds[2], GemIds[3]);
        stream.WritePisc(GemIds[4], GemIds[5], GemIds[6], GemIds[7]);
        stream.WritePisc(GemIds[8], GemIds[9], GemIds[10], GemIds[11]);
        stream.WritePisc(GemIds[12], GemIds[13], GemIds[14], GemIds[15]); // в 3+ длина данных 36 (когда нет информации), в 1.2 было 56
        stream.WritePisc(GemIds[16], GemIds[17]);                         // 39 - 3.5.0.3
    }

    // не используется
    public override void ReadAdditionalDetails(PacketStream stream)
    {
        GemIds[0] = stream.ReadUInt32();  // Can modify the appearance, TemplateId предмета для внешнего вида
        ImageItemTemplateId = GemIds[0];

        Durability = stream.ReadByte();   // durability
        _ = stream.ReadUInt16();          // unk

        GemIds[1] = stream.ReadUInt32();  // Luna Gem, TemplateId EnchantingGem - Позволяет зачаровать предмет снаряжения.
        RuneId = (ushort)GemIds[1];

        ChargeTime = stream.ReadDateTime(); // ChargeStartTime
        if (Template.BindType == ItemBindType.BindOnUnpack)
            UnpackTime = ChargeTime;
        else
            ChargeStartTime = ChargeTime;

        GemIds[2] = stream.ReadUInt32();  //
        GemIds[4] = stream.ReadUInt32();  // 1 crescent stone, TemplateId Socket - Позволяет придать предмету снаряжения дополнительные свойства.
        GemIds[5] = stream.ReadUInt32();  // 2 crescent stone
        GemIds[6] = stream.ReadUInt32();  // 3 crescent stone
        GemIds[7] = stream.ReadUInt32();  // 4 crescent stone
        GemIds[8] = stream.ReadUInt32();  // 5 crescent stone
        GemIds[9] = stream.ReadUInt32();  // 6 crescent stone
        GemIds[10] = stream.ReadUInt32(); // 7 crescent stone
        GemIds[11] = stream.ReadUInt32(); // 8 crescent stone
        GemIds[12] = stream.ReadUInt32(); // 9 crescent stone

        TemperPhysical = stream.ReadUInt16(); // TemperPhysical
        TemperMagical = stream.ReadUInt16(); // TemperMagical

        GemIds[3] = stream.ReadUInt32();  // RemainingExperience
        RemainingExperience = GemIds[3];

        GemIds[13] = stream.ReadUInt32(); // 0 Additional Effects
        GemIds[14] = stream.ReadUInt32(); // 1 Additional Effects
        GemIds[15] = stream.ReadUInt32(); // 2 Additional Effects
        GemIds[16] = stream.ReadUInt32(); // 3 Additional Effects
        GemIds[17] = stream.ReadUInt32(); // 4 Additional Effects
    }

    // используется при ремонте и улучшении предметов
    public override void WriteAdditionalDetails(PacketStream stream)
    {
        NormalizeSynthesisExperienceForSerialization();

        GemIds[0] = ImageItemTemplateId;
        stream.Write(GemIds[0]);   // for transformation, Can modify the appearance, TemplateId предмета для внешнего вида
        stream.Write(Durability);  // durability
        stream.Write(ChargeCount); // ChargeCount mb

        GemIds[1] = RuneId;
        stream.Write(GemIds[1]);   // Luna Gem, TemplateId EnchantingGem - Позволяет зачаровать предмет снаряжения.

        stream.Write(Template.BindType == ItemBindType.BindOnUnpack ? UnpackTime : ChargeStartTime);

        GemIds[2] = (uint)EfainCubeId;
        stream.Write(GemIds[2]);  // эффект эфенских кубов
        // смещение 24 для версии 3+, 5+
        stream.Write(GemIds[4]);  // 1 crescent stone, TemplateId Socket - Позволяет придать предмету снаряжения дополнительные свойства.
        stream.Write(GemIds[5]);  // 2 crescent stone
        stream.Write(GemIds[6]);  // 3 crescent stone
        stream.Write(GemIds[7]);  // 4 crescent stone
        stream.Write(GemIds[8]);  // 5 crescent stone
        stream.Write(GemIds[9]);  // 6 crescent stone
        stream.Write(GemIds[10]); // 7 crescent stone
        stream.Write(GemIds[11]); // 7 crescent stone
        stream.Write(GemIds[12]); // 7 crescent stone

        stream.Write(TemperPhysical);  // Записываем TemperPhysical
        stream.Write(TemperMagical);   // Записываем TemperMagical

        GemIds[3] = RemainingExperience;
        stream.Write(GemIds[3]);  // RemainingExperience

        stream.Write(GemIds[13]); // 0 Additional Effects
        stream.Write(GemIds[14]); // 1 Additional Effects
        stream.Write(GemIds[15]); // 2 Additional Effects
        stream.Write(GemIds[16]); // 3 Additional Effects
        stream.Write(GemIds[17]); // 4 Additional Effects
    }

    private void NormalizeSynthesisExperienceForSerialization()
    {
        if (Template is not EquipItemTemplate equipTemplate)
            return;

        var categoryId = equipTemplate.ItemRndAttrCategoryId;

        // TODO будет исправлено в будущем, когда будет добавлена поддержка синтеза и категории для предметов

        //if (categoryId <= 0 || ItemGameData.Instance.GetItemRndAttrCategory(categoryId) == null)
        //    return;

        //if (!ItemGameData.Instance.IsValidSynthesisGrade(categoryId, Grade))
        //    return;

        int rawExp = RemainingExperience > int.MaxValue ? int.MaxValue : (int)RemainingExperience;
        //var normalizedExp = ItemGameData.Instance.NormalizeCumulativeSynthesisExperience(rawExp, categoryId, Grade);
        //RemainingExperience = (uint)normalizedExp;
    }

    /// <summary>
    /// Clears the evolved attribute values cache when evolved attributes are modified
    /// </summary>
    public void ClearEvolvedAttributeCache()
    {
        EvolvedAttributeValues?.Clear();
    }
}
