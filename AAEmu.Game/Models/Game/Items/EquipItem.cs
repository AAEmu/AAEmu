using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.GameData;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

public class EquipItem : Item
{
    public override ItemDetailType DetailType => ItemDetailType.Equipment;

    public byte Durability { get; set; }

    /// <summary>
    /// Lunastone applied to the piece. This used to occupy the u16 at detail struct+0x3c on the
    /// strength of the 1.2 field order, but that word is what the client reads as the tempering
    /// scale, and a template id does not survive being clamped like one. Nothing reads this value
    /// back, so it keeps its own slot in the detail blob rather than sharing.
    /// </summary>
    public uint RuneId { get; set; }

    /// <summary>
    /// The lunagems socketed into the piece, by item template id, in socket order.
    /// </summary>
    /// <remarks>
    /// Held in the detail block's socket run - <see cref="SocketSlots"/> values from
    /// <see cref="SocketFirstSlot"/>. This array used to live only in memory: it was filled when a
    /// gem was set and read when gear bonuses were totalled, but never written into the block, so
    /// the gems reached neither the database nor the client and were gone at the next restart.
    /// </remarks>
    public uint[] GemIds { get; set; }

    /// <summary>
    /// Tempering step the item currently sits at - an <c>enchant_scale_ratios</c> row id, so 0 is
    /// untempered and the ceiling is the template's <see cref="ItemTemplate.MaxEnchantScaleId"/>.
    /// </summary>
    /// <remarks>
    /// Rides the u16 the detail serializer only calls <c>type</c>, at struct+0x3c. The awakening
    /// preview reads that same word as a scale - it compares it against a bound and runs it through
    /// a clamp to build the "+3 ▶ +5" line  - which is what a
    /// generic <c>type</c> name and a 0-31 row id both fit. The 1.2 layout put a rune id here
    /// instead; see <see cref="RuneId"/> for where that went.
    /// </remarks>
    public ushort EnchantScale { get; set; }

    /// <summary>
    /// Synthesis experience banked toward the next grade. Per section, not cumulative:
    /// item_rnd_attr_category_properties carries a <c>req_exp</c> for every grade and the tooltip
    /// prints this against it as "EXP here/needed".
    /// </summary>
    public uint EvolvingExp { get; set; }

    /// <summary>
    /// The "Synthesis Effect" lines the item carries, as <c>item_rnd_attr_unit_modifier_groups</c> ids.
    /// </summary>
    /// <remarks>
    /// Only the group is kept, never a magnitude: what an effect is worth is looked up from
    /// <c>item_rnd_attr_unit_modifiers</c> for that group at the item's current grade, which is why the
    /// same effect grows as the piece is synthesised further. Storing rolled values instead leaves the
    /// client with nothing to render, since it does that lookup itself.
    /// </remarks>
    public uint[] RndAttrGroupIds { get; set; } = new uint[RndAttrSlots];

    /// <summary>The occupied effect slots, in order.</summary>
    public IEnumerable<uint> UsedRndAttrGroupIds => (RndAttrGroupIds ?? []).Where(id => id != 0);

    /// <summary>
    /// Whether a failed awakening or temper has locked the item. A locked item cannot be enchanted
    /// again until a restore item clears it.
    /// </summary>
    public bool EnchantDisabled
    {
        get => ItemFlags.HasFlag(ItemFlag.EnchantDisabled);
        set => ItemFlags = value ? ItemFlags | ItemFlag.EnchantDisabled : ItemFlags & ~ItemFlag.EnchantDisabled;
    }

    /// <summary>
    /// Physical stat multiplier the tempering step is worth, in percent, for the damage and armor
    /// formulas in <see cref="Weapon"/> and <see cref="Armor"/> - which only apply it above 100.
    /// The shipped ladder carries scale 10 per step, so +12 lands at 112 (a 12% bonus).
    /// </summary>
    public ushort TemperPhysical => ScaleMultiplier;

    /// <summary>Magical counterpart of <see cref="TemperPhysical"/>; the same step drives both.</summary>
    public ushort TemperMagical => ScaleMultiplier;

    private ushort ScaleMultiplier
    {
        get
        {
            if (EnchantScale == 0)
                return 0;
            var scale = ItemEnchantGameData.Instance.GetEnchantScaleValue((byte)EnchantScale);
            return (ushort)(100 + scale / 10);
        }
    }

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

    /// <summary>
    /// Where the remaining per-item enchant state rides inside the 18-value pisc block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's equipment detail struct has four standalone dwords, at struct +0x01, +0x08,
    /// +0x14 and +0x40, followed by nine socket dwords from +0x18 and five more from +0x44; the
    /// codec emits them as slots 0-3, 4-12 and 13-17 in that order . Slot 0
    /// is the image item and slot 2 the dye, both already established.
    /// </para>
    /// <para>
    /// Slot 3 is the synthesis experience and slots 13 to 17 are its effect lines. The experience is
    /// nailed down: the detail struct starts at
    /// item+0x20 (the item serializer hands that offset as the detail base), and the
    /// tooltip builds its <c>minExp</c> line from <c>dword [item+0x60]</c> - the same dword
    /// . That is the left half of the "EXP 1234/5000" line the synthesis tooltip
    /// shows. The right half is not stored per item at all: the client keys
    /// item_rnd_attr_category_properties by the item's own grade and reads <c>req_exp</c> off it, so
    /// the synthesis grade IS <see cref="Item.Grade"/> and nothing extra needs saving for it.
    /// </para>
    /// <para>
    /// The rolled attributes have no observed home; the trailing slots nothing else claims are as
    /// good a guess as any and are what to move if they show up wrong.
    /// </para>
    /// </remarks>
    private const int DyeItemIdSlot = 2;
    private const int EvolvingExpSlot = 3;
    private const int RuneIdSlot = 1;
    private const int SocketFirstSlot = 4;
    private const int RndAttrFirstSlot = 13;

    /// <summary>
    /// Sockets the detail block reserves room for. The client's own chance table carries ten columns,
    /// but the block's run between the dye and the effect lines is nine, and no shipped
    /// item_socket_num_limits row asks for more than six.
    /// </summary>
    public const int SocketSlots = 9;

    /// <summary>
    /// How many synthesis effects an item can carry - the run of slots the block reserves for them.
    /// </summary>
    public const int RndAttrSlots = 5;

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
        GemIds = new uint[SocketSlots];
        GemData = new uint[18];
    }

    public EquipItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
    {
        GemIds = new uint[SocketSlots];
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
        EnchantScale = stream.ReadUInt16(); // struct+0x3c, the client's tempering scale
        EvolveChance = stream.ReadUInt16();
        ChargeProcTime = stream.ReadDateTime();
        MappingFailBonus = stream.ReadByte();
        ElementLevel = stream.ReadByte();
        GemData = stream.ReadPisc(18);
        // WriteDetails puts ImageItemTemplateId into the first pisc slot and the dye into the third,
        // so read both back out of the same ones. Without this the round trip is asymmetric, and
        // since WriteDetails is also what persists items.details, a restart replaced every stored
        // value with zero.
        ImageItemTemplateId = GemData[0];
        DyeItemId = GemData[DyeItemIdSlot];
        EvolvingExp = GemData[EvolvingExpSlot];
        RuneId = GemData[RuneIdSlot];

        GemIds = new uint[SocketSlots];
        for (var i = 0; i < SocketSlots; i++)
            GemIds[i] = GemData[SocketFirstSlot + i];

        RndAttrGroupIds = new uint[RndAttrSlots];
        for (var i = 0; i < RndAttrSlots; i++)
            RndAttrGroupIds[i] = GemData[RndAttrFirstSlot + i];
    }

    public override void WriteDetails(PacketStream stream)
    {
        stream.Write(Durability);          // durability u8
        stream.Write((ushort)ChargeCount); // chargeCount u16 (binary serializer vtbl+168, 2 bytes)
        stream.Write(ChargeStartTime);     // chargeTime i64
        stream.Write(EnchantScale);        // struct+0x3c, tempering scale
        stream.Write(EvolveChance);        // evolveChance u16
        stream.Write(ChargeProcTime);      // chargeProcTime i64
        stream.Write(MappingFailBonus);    // mappingFailBonus u8
        stream.Write(ElementLevel);        // elementLevel u8
        // then 14 gem ints. ImageItemTemplateId must occupy GemData[0] on the wire.
        var gemData = GemData ?? new uint[18];
        if (gemData.Length < 18)
            Array.Resize(ref gemData, 18);
        gemData[0] = ImageItemTemplateId;
        // An undyed but dyeable piece falls back to the colour its own template carries
        // (dyeable_items.color, loaded into EquipItemTemplate.DyeingColor). The client only draws
        // the colour line in the tooltip when this value is non-zero, so without the fallback the
        // line appeared solely on pieces that had actually been dyed.
        gemData[DyeItemIdSlot] = DyeItemId != 0
            ? DyeItemId
            : (Template as EquipItemTemplate)?.DyeingColor ?? 0u;
        gemData[EvolvingExpSlot] = EvolvingExp;
        gemData[RuneIdSlot] = RuneId;
        var gems = GemIds ?? [];
        for (var i = 0; i < SocketSlots; i++)
            gemData[SocketFirstSlot + i] = i < gems.Length ? gems[i] : 0u;

        var groupIds = RndAttrGroupIds ?? [];
        for (var i = 0; i < RndAttrSlots; i++)
            gemData[RndAttrFirstSlot + i] = i < groupIds.Length ? groupIds[i] : 0u;
        stream.WritePisc(gemData);
    }
}
