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
    private DateTime _chargeStartTime = DateTime.MinValue;
    private int _chargeCount;

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
    public virtual uint DetailBytesLength { get; } = 0;

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
    public DateTime ChargeUseSkillTime { get; set; }

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
    public DateTime ChargeStartTime
    {
        get => _chargeStartTime;
        set
        {
            if (_chargeStartTime == value)
                return;
            _chargeStartTime = value;
            _isDirty = true;
        }
    }

    [JsonProperty]
    public int ChargeCount
    {
        get => _chargeCount;
        set
        {
            if (_chargeCount == value)
                return;
            _chargeCount = value;
            _isDirty = true;
        }
    }

    [JsonProperty]
    public virtual ItemDetailType DetailType { get; set; }

    [JsonProperty]
    public byte[] Detail { get; set; }

    // Helper
    [JsonIgnore]
    public ItemContainer _holdingContainer { get; set; }

    public static uint DawnStone => 327;
    public static uint Coins => 500;
    /// <summary>농민의 주머니 — lowest NPC coin-purse tier (opens loot pack 10867).</summary>
    public static uint FarmerCoinPurse => 29203;
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
        _holdingContainer = null;
        _isDirty = true;
    }

    public Item(byte worldId)
    {
        WorldId = worldId;
        OwnerId = 0;
        Slot = -1;
        _holdingContainer = null;
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
        _holdingContainer = null;
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
        _holdingContainer = null;
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
        MadeUnitId = (uint)stream.ReadUInt64(); // v10: madeUnitId is 8 bytes on the wire
        WorldId = stream.ReadByte();
        UnsecureTime = stream.ReadDateTime();
        UnpackTime = stream.ReadDateTime();
        ChargeUseSkillTime = stream.ReadDateTime(); // v10: new trailing field
    }

    public override PacketStream Write(PacketStream stream)
    {
        return Write(stream, Count);
    }

    /// <summary>
    /// Writes the canonical item body while publishing a caller-selected stack count.
    /// This is used by trade offer packets, whose item snapshot represents only the offered units.
    /// </summary>
    public PacketStream Write(PacketStream stream, int count)
    {
        stream.Write(TemplateId);
        if (TemplateId == 0)
            return stream;
        stream.Write(Id);
        stream.Write(Grade);
        stream.Write((byte)ItemFlags); //bounded
        stream.Write(count);
        stream.Write((byte)DetailType);
        WriteDetails(stream);
        stream.Write(CreateTime);
        stream.Write(LifespanMins);
        stream.Write((ulong)MadeUnitId); // v10: madeUnitId is 8 bytes on the wire
        stream.Write(WorldId);
        stream.Write(UnsecureTime);
        stream.Write(UnpackTime);
        stream.Write(ChargeUseSkillTime); // v10: new trailing field
        return stream;
    }

    // Detail-blob body length (the bytes after the leading detailType byte, which Item.Read/Write emits)
    // for items handled by the base Item serializer. Per the 10.0.2.13 item-detail serializer
    // blob keyed on the detailType byte. Equipment is structured (EquipItem); Slave/Mate/Ucc/Treasure/
    // BigFish/MusicSheet have dedicated subclasses; the rest round-trip through this base path.
    private static int GetDetailBodyLength(ItemDetailType detailType) => (byte)detailType switch
    {
        2 => 33,         // Slave               (total 34)
        3 => 20,         // Mate                (total 21)
        4 => 9,          // Ucc                 (total 10)
        5 or 11 => 24,   // Treasure / Location (total 25)
        6 or 7 => 16,    // BigFish / Decoration (total 17)
        8 or 14 => 8,    // MusicSheet / type 0xE (total 9)
        9 => 4,          // Glider              (total 5)
        10 => 12,        // SlaveEquipment      (total 13)
        12 => 10,        // type 0xC            (total 11)
        13 => 13,        // type 0xD            (total 14)
        _ => 0,          // Equipment(1) is structured (EquipItem); 0/unknown carries no body
    };

    public virtual void ReadDetails(PacketStream stream)
    {
        var length = GetDetailBodyLength(DetailType);
        if (length > 0)
            Detail = stream.ReadBytes(length);
    }

    public virtual void WriteDetails(PacketStream stream)
    {
        var length = GetDetailBodyLength(DetailType);
        if (length <= 0)
            return;
        stream.Write(Detail?.Length == length ? Detail : new byte[length]);
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
