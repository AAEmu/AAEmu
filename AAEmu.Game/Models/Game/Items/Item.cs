using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;
using Newtonsoft.Json;

namespace AAEmu.Game.Models.Game.Items;

[JsonObject(MemberSerialization.OptIn)]
public class Item : PacketMarshaler, IComparable<Item>
{
    private byte _worldId;
    private ulong _ownerId;
    private ulong _id;
    private uint _templateId;
    private SlotType _slotType;
    private int _slot;
    private byte _grade;
    private ItemFlag _itemFlags;
    private int _count;
    private int _lifespanMins;
    private uint _madeUnitId;
    private DateTime _createTime;
    private DateTime _unsecureTime;
    private DateTime _unpackTime;
    private uint _imageItemTemplateId;
    private bool _isDirty;
    private ulong _uccId;
    private DateTime _expirationTime;
    private double _expirationOnlineMinutesLeft;
    private DateTime _chargeUseSkillTime;
    private short _chargeCount;
    private uint[] _gemIds;
    private byte _durability;
    private ushort _TemperPhysical;
    private ushort _TemperMagical;
    private uint _runeId;
    private DateTime _chargeTime;
    private DateTime _freshnessTime;

    [JsonIgnore]
    public bool IsDirty { get => _isDirty; set => _isDirty = value; }

    [JsonProperty]
    public byte WorldId { get => _worldId; set { _worldId = value; _isDirty = true; } }

    [JsonProperty]
    public ulong OwnerId { get => _ownerId; set { _ownerId = value; _isDirty = true; } }

    [JsonProperty]
    public ulong Id { get => _id; set { _id = value; _isDirty = true; } }

    [JsonProperty]
    public uint TemplateId { get => _templateId; set { _templateId = value; _isDirty = true; } }

    [JsonIgnore]
    public ItemTemplate Template { get; set; }

    [JsonProperty]
    public virtual uint DetailBytesLength { get; }

    [JsonProperty]
    public SlotType SlotType { get => _slotType; set { _slotType = value; _isDirty = true; } }

    [JsonProperty]
    public int Slot { get => _slot; set { _slot = value; _isDirty = true; } }

    [JsonProperty]
    public byte Grade { get => _grade; set { _grade = value; _isDirty = true; } }

    [JsonProperty]
    public ItemFlag ItemFlags { get => _itemFlags; set { _itemFlags = value; _isDirty = true; } }

    [JsonProperty]
    public int Count { get => _count; set { _count = value; _isDirty = true; } }

    [JsonProperty]
    public int LifespanMins { get => _lifespanMins; set { _lifespanMins = value; _isDirty = true; } }

    [JsonProperty]
    public uint MadeUnitId { get => _madeUnitId; set { _madeUnitId = value; _isDirty = true; } }

    [JsonProperty]
    public DateTime CreateTime { get => _createTime; set { _createTime = value; _isDirty = true; } }

    [JsonProperty]
    public DateTime UnsecureTime { get => _unsecureTime; set { _unsecureTime = value; _isDirty = true; } }

    [JsonProperty]
    public DateTime UnpackTime { get => _unpackTime; set { _unpackTime = value; _isDirty = true; } }

    [JsonProperty]
    public uint ImageItemTemplateId { get => _imageItemTemplateId; set { _imageItemTemplateId = value; _isDirty = true; } }

    /// <summary>
    /// Internal representation of the exact time a item will expire (UTC)
    /// </summary>
    [JsonProperty]
    public DateTime ExpirationTime
    {
        get => _expirationTime;
        set
        {
            if (_expirationTime != value)
            {
                _expirationTime = value;
                _isDirty = true;
            }
        }
    }

    /// <summary>
    /// Internal representation of the time this item has left before expiring, only counting down if the owning character is online
    /// </summary>
    [JsonProperty]
    public double ExpirationOnlineMinutesLeft
    {
        get => _expirationOnlineMinutesLeft;
        set
        {
            _expirationOnlineMinutesLeft = value;
            _isDirty = true;
        }
    }

    [JsonProperty]
    public ulong UccId
    {
        get => _uccId;
        set
        {
            _uccId = value;
            if (value > 0)
                SetFlag(ItemFlag.HasUCC);
            else
                RemoveFlag(ItemFlag.HasUCC);
            _isDirty = true;
        }
    }

    [JsonProperty]
    public DateTime ChargeStartTime { get; set; } = DateTime.MinValue;

    [JsonProperty]
    public virtual ItemDetailType DetailType { get; set; } // TODO 1.0 max type: 8, at 1.2 max type 9, at 3.0.3.0 max type 10, at 3.5.0.3 max type 12, at 5.7 max type 13

    [JsonProperty]
    public DateTime ChargeUseSkillTime { get => _chargeUseSkillTime; set { _chargeUseSkillTime = value; _isDirty = true; } }

    [JsonProperty]
    public byte Durability { get => _durability; set { _durability = value; _isDirty = true; } }

    [JsonProperty]
    public short ChargeCount { get => _chargeCount; set { _chargeCount = value; _isDirty = true; } }

    [JsonProperty]
    public DateTime ChargeTime { get => _chargeTime; set { _chargeTime = value; _isDirty = true; } }

    [JsonProperty]
    public DateTime FreshnessTime { get => _freshnessTime; set { _freshnessTime = value; _isDirty = true; } }

    [JsonProperty]
    public ushort TemperPhysical { get => _TemperPhysical; set { _TemperPhysical = value; _isDirty = true; } }

    [JsonProperty]
    public ushort TemperMagical { get => _TemperMagical; set { _TemperMagical = value; _isDirty = true; } }

    [JsonProperty]
    public uint RuneId { get => _runeId; set { _runeId = value; _isDirty = true; } }

    [JsonProperty]
    public uint[] GemIds // 18 + 4 = 22 in 3.5.0.3 & 5.0.7.0, 16 in 3.0.3.0, 7 in 1.2
    {
        get => _gemIds ??= new uint[GemIdMaxCount];
        set { _gemIds = value; _isDirty = true; }
    }

    public int GemIdMaxCount = 18; // 18 + 4 = 22 in 3.5.0.3 & 5.0.7.0, 16 in 3.0.3.0, 7 in 1.2
    [JsonProperty]
    public byte[] Detail { get; set; }

    // Helper
    [JsonIgnore]
    public ItemContainer HoldingContainer { get; set; }

    public static uint DawnStone => 327;
    public static uint Coins => 500;
    public static uint TaxCertificate => 31891;
    public static uint BoundTaxCertificate => 31892;
    public static uint AppraisalCertificate => 28085;
    public static uint CrestStamp => 17662;
    public static uint CrestInk => 17663;
    public static uint SheetMusic => 28051;
    public static uint SalonCertificate => 30811;
    public static uint TreasureMapWithCoordinates => 24581;

    /// <summary>
    /// Sort will use itemSlot numbers
    /// </summary>
    /// <param name="otherItem"></param>
    /// <returns></returns>
    public int CompareTo(Item otherItem)
    {
        if (otherItem == null) return 1;
        return this.Slot.CompareTo(otherItem.Slot);
    }

    public Item()
    {
        WorldId = AppConfiguration.Instance.Id;
        OwnerId = 0;
        Slot = -1;
        HoldingContainer = null;
        _isDirty = true;
    }

    public Item(byte worldId)
    {
        WorldId = worldId;
        OwnerId = 0;
        Slot = -1;
        HoldingContainer = null;
        _isDirty = true;
    }

    public Item(ulong id, ItemTemplate template, int count)
    {
        WorldId = AppConfiguration.Instance.Id;
        OwnerId = 0;
        Id = id;
        TemplateId = template.Id;
        Template = template;
        Count = count;
        Slot = -1;
        HoldingContainer = null;
        _isDirty = true;
    }

    public Item(byte worldId, ulong id, ItemTemplate template, int count)
    {
        WorldId = worldId;
        OwnerId = 0;
        Id = id;
        TemplateId = template.Id;
        Template = template;
        Count = count;
        Slot = -1;
        HoldingContainer = null;
        _isDirty = true;
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

        DetailType = (ItemDetailType)stream.ReadByte();
        ReadDetails(stream);

        CreateTime = stream.ReadDateTime();
        LifespanMins = stream.ReadInt32();
        MadeUnitId = stream.ReadUInt32();
        WorldId = stream.ReadByte();
        UnsecureTime = stream.ReadDateTime();
        UnpackTime = stream.ReadDateTime();
        ChargeUseSkillTime = stream.ReadDateTime(); // added in 1.7
    }

    public override PacketStream Write(PacketStream stream)
    {
        stream.Write(TemplateId); // type
        if (TemplateId == 0)
            return stream;

        stream.Write(Id);    // id
        stream.Write(Grade); // grade
        stream.Write((byte)ItemFlags); // flags | bounded
        stream.Write(Count); // stackSize

        stream.Write((byte)DetailType); // detailType
        WriteDetails(stream);

        stream.Write(CreateTime);
        stream.Write(LifespanMins);
        stream.Write(MadeUnitId);
        stream.Write(WorldId);
        stream.Write(UnsecureTime);
        stream.Write(UnpackTime);
        stream.Write(ChargeUseSkillTime); // added in 1.7

        return stream;
    }

    public virtual void ReadDetails(PacketStream stream)
    {
        var mDetailLength = 0;
        switch (DetailType)
        {
            case ItemDetailType.Equipment: // 1
                mDetailLength = 39; // есть расшифровка в items/EquipItem, в 3.5.0.3 - 39, в 3.0.3.0 длина данных 36 (когда нет информации), в 1.2 было 56
                break;
            case ItemDetailType.Slave: // 2
                mDetailLength = 30; // есть расшифровка в items/SummonSlave
                break;
            case ItemDetailType.Mate: // 3
                mDetailLength = 21; // in 1.2 - 7, in 3+ - 21 - есть расшифровка в items/SummonMate
                break;
            case ItemDetailType.Ucc: // 4
                mDetailLength = 10; // есть расшифровка в items/UccItem
                break;
            case ItemDetailType.Treasure: // 5
            case ItemDetailType.Location: // 11
                mDetailLength = 25;
                break;
            case ItemDetailType.BigFish: // 6
            case ItemDetailType.Decoration: // 7
                mDetailLength = 17; // есть расшифровка в items/BigFish
                break;
            case ItemDetailType.MusicSheet: // 8
                mDetailLength = 9; // есть расшифровка в items/MusicSheetItem
                break;
            case ItemDetailType.Glider: // 9
                mDetailLength = 5;
                break;
            case ItemDetailType.SlaveEquipment: // 10
                mDetailLength = 13; // есть расшифровка в items/SlaveEquip, нет в 1.2
                break;
            case ItemDetailType.ItemDetailType12: // 12
                mDetailLength = 12; // 12 in 3.5, 4.5, 11 in 5.0
                break;
            case ItemDetailType.Invalid:
            default:
                break;
        }

        mDetailLength -= 1;
        if (mDetailLength > 0)
        {
            Detail = stream.ReadBytes(mDetailLength);
        }
    }

    public virtual void WriteDetails(PacketStream stream)
    {
        var mDetailLength = 0;
        switch (DetailType)
        {
            case ItemDetailType.Equipment:
                mDetailLength = 39;  // есть расшифровка в items/EquipItem, в 3.5.0.3 - 39, в 3.0.3.0 длина данных 36 (когда нет информации), в 1.2 было 56
                break;
            case ItemDetailType.Slave:
                mDetailLength = 30;
                break;
            case ItemDetailType.Mate:
                mDetailLength = 21; // in 1.2 - 7, in 3+ - 21 - есть расшифровка в items/SummonMate
                break;
            case ItemDetailType.Ucc:
                mDetailLength = 10; // есть расшифровка в items/UccItem
                break;
            case ItemDetailType.Treasure:
            case ItemDetailType.Location: // нет в 1.2
                mDetailLength = 25;
                break;
            case ItemDetailType.BigFish: // есть расшифровка в items/BigFish
            case ItemDetailType.Decoration:
                mDetailLength = 17;
                break;
            case ItemDetailType.MusicSheet:
                mDetailLength = 9; // есть расшифровка в items/MusicSheetItem
                break;
            case ItemDetailType.Glider:
                mDetailLength = 5;
                break;
            case ItemDetailType.SlaveEquipment: // есть расшифровка в items/SlaveEquip, нет в 1.2
                mDetailLength = 13;
                break;
            case ItemDetailType.ItemDetailType12:
                mDetailLength = 12;
                break;
        }
        mDetailLength -= 1;
        if (mDetailLength > 0)
        {
            Detail = new byte[mDetailLength];
            stream.Write(Detail);
        }
    }

    public virtual void ReadAdditionalDetails(PacketStream stream)
    {
    }

    public virtual void WriteAdditionalDetails(PacketStream stream)
    {
    }

    public virtual bool HasFlag(ItemFlag flag)
    {
        return (ItemFlags & flag) == flag;
    }

    public virtual void SetFlag(ItemFlag flag)
    {
        ItemFlags |= flag;
    }

    public virtual void RemoveFlag(ItemFlag flag)
    {
        ItemFlags &= ~flag;
    }

    /// <summary>
    /// Called just before a item is getting destroyed
    /// </summary>
    public virtual void OnManuallyDestroyingItem()
    {
        //
    }

    public virtual bool CanDestroy()
    {
        return true;
    }
}
