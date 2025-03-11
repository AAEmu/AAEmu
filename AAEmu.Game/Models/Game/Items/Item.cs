using System;
using System.Linq;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Containers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items;

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
    private byte _flags;
    private byte _durability;
    private short _chargeCount;
    private ushort _TemperPhysical;
    private ushort _TemperMagical;
    private uint _runeId;
    private DateTime _chargeTime;

    public bool IsDirty { get => _isDirty; set => _isDirty = value; }
    public byte WorldId { get => _worldId; set { _worldId = value; _isDirty = true; } }
    public ulong OwnerId { get => _ownerId; set { _ownerId = value; _isDirty = true; } }
    public ulong Id { get => _id; set { _id = value; _isDirty = true; } }
    public uint TemplateId { get => _templateId; set { _templateId = value; _isDirty = true; } }
    public ItemTemplate Template { get; set; }
    public virtual uint DetailBytesLength { get; set; } = 0;
    public SlotType SlotType { get => _slotType; set { _slotType = value; _isDirty = true; } }
    public int Slot { get => _slot; set { _slot = value; _isDirty = true; } }
    public byte Grade { get => _grade; set { _grade = value; _isDirty = true; } }
    public ItemFlag ItemFlags { get => _itemFlags; set { _itemFlags = value; _isDirty = true; } }
    public int Count { get => _count; set { _count = value; _isDirty = true; } }
    public int LifespanMins { get => _lifespanMins; set { _lifespanMins = value; _isDirty = true; } }
    public uint MadeUnitId { get => _madeUnitId; set { _madeUnitId = value; _isDirty = true; } }
    public DateTime CreateTime { get => _createTime; set { _createTime = value; _isDirty = true; } }
    public DateTime UnsecureTime { get => _unsecureTime; set { _unsecureTime = value; _isDirty = true; } }
    public DateTime UnpackTime { get => _unpackTime; set { _unpackTime = value; _isDirty = true; } }
    public uint ImageItemTemplateId { get => _imageItemTemplateId; set { _imageItemTemplateId = value; _isDirty = true; } }

    /// <summary>
    /// Internal representation of the exact time a item will expire (UTC)
    /// </summary>
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
    public double ExpirationOnlineMinutesLeft
    {
        get => _expirationOnlineMinutesLeft;
        set
        {
            _expirationOnlineMinutesLeft = value;
            _isDirty = true;
        }
    }

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

    public DateTime ChargeStartTime { get; set; } = DateTime.MinValue;
    public virtual ItemDetailType DetailType { get; set; } // TODO 1.0 max type: 8, at 1.2 max type 9, at 3.0.3.0 max type 10, at 3.5.0.3 max type 12, at 5.7 max type 13
    public DateTime ChargeUseSkillTime { get => _chargeUseSkillTime; set { _chargeUseSkillTime = value; _isDirty = true; } }
    public byte Flags { get => _flags; set { _flags = value; _isDirty = true; } }
    public byte Durability { get => _durability; set { _durability = value; _isDirty = true; } }
    public short ChargeCount { get => _chargeCount; set { _chargeCount = value; _isDirty = true; } }
    public DateTime ChargeTime { get => _chargeTime; set { _chargeTime = value; _isDirty = true; } }
    public ushort TemperPhysical { get => _TemperPhysical; set { _TemperPhysical = value; _isDirty = true; } }
    public ushort TemperMagical { get => _TemperMagical; set { _TemperMagical = value; _isDirty = true; } }
    public uint RuneId { get => _runeId; set { _runeId = value; _isDirty = true; } }
    public DateTime ChargeProcTime { get; set; }
    public byte MappingFailBonus { get; set; }

    public uint[] GemIds { get; set; }
    public byte[] Detail { get; set; }

    // Helper
    public ItemContainer _holdingContainer { get; set; }

    public static uint Coins => 500;
    public static uint TaxCertificate => 31891;
    public static uint BoundTaxCertificate => 31892;
    public static uint AppraisalCertificate => 28085;
    public static uint CrestStamp => 17662;
    public static uint CrestInk => 17663;
    public static uint SheetMusic => 28051;
    public static uint SalonCertificate => 30811;

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
        GemIds = new uint[18];
    }

    public Item(byte worldId)
    {
        WorldId = worldId;
        OwnerId = 0;
        Slot = -1;
        _holdingContainer = null;
        _isDirty = true;
        GemIds = new uint[18];
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
        GemIds = new uint[18];
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
        GemIds = new uint[18];
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
                //mDetailLength = 36; // есть расшифровка в items/EquipItem, в 3+ длина данных 36 (когда нет информации), в 1.2 было 56
                Durability = stream.ReadByte();       // durability
                ChargeCount = stream.ReadInt16();     // chargeCount
                ChargeTime = stream.ReadDateTime();   // chargeTime
                TemperPhysical = stream.ReadUInt16(); // scaledA
                TemperMagical = stream.ReadUInt16();  // scaledB
                ChargeProcTime = stream.ReadDateTime(); // chargeProcTime
                MappingFailBonus = stream.ReadByte();   // mappingFailBonus - нет в 4.5.2.6, есть в 4.5.1.0 и 5.7

                var mGems = stream.ReadPiscW(GemIds.Length);
                GemIds = mGems.Select(id => (uint)id).ToArray();
                //var mGems = stream.ReadPisc(4);
                //GemIds[0] = (uint)mGems[0];
                //GemIds[1] = (uint)mGems[1];
                //GemIds[2] = (uint)mGems[2];
                //GemIds[3] = (uint)mGems[3];

                //mGems = stream.ReadPisc(4);
                //GemIds[4] = (uint)mGems[0];
                //GemIds[5] = (uint)mGems[1];
                //GemIds[6] = (uint)mGems[2];
                //GemIds[7] = (uint)mGems[3];

                //mGems = stream.ReadPisc(4);
                //GemIds[8] = (uint)mGems[0];
                //GemIds[9] = (uint)mGems[1];
                //GemIds[10] = (uint)mGems[2];
                //GemIds[11] = (uint)mGems[3];

                //mGems = stream.ReadPisc(4);
                //GemIds[12] = (uint)mGems[0];
                //GemIds[13] = (uint)mGems[1];
                //GemIds[14] = (uint)mGems[2];
                //GemIds[15] = (uint)mGems[3];
                //mGems = stream.ReadPisc(2);
                //GemIds[16] = (uint)mGems[0];
                //GemIds[17] = (uint)mGems[1];
                break;
            case ItemDetailType.Slave: // 2
                mDetailLength = 34; // есть расшифровка в items/SummonSlave 30 in 3.5, 4.5.2.6, 34 in 4.5.1.0, 5.7
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
                mDetailLength = 13;
                break;
            case ItemDetailType.Unk12: // 12
                mDetailLength = 11; // 12 in 3.5, 4.5, 11 in 5.0
                break;
            case ItemDetailType.Unk13: // 13
                mDetailLength = 14; // added in 5.7, 4.5
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
                //mDetailLength = 36; // есть расшифровка в items/EquipItem, в 3+ длина данных 36 (когда нет информации), в 1.2 было 56
                stream.Write(Durability);     // durability
                stream.Write(ChargeCount);    // chargeCount
                stream.Write(ChargeTime);     // chargeTime
                stream.Write(TemperPhysical); // scaledA
                stream.Write(TemperMagical);  // scaledB
                stream.Write(ChargeProcTime);   // chargeProcTime
                stream.Write(MappingFailBonus); // mappingFailBonus - нет в 4.5.2.6, есть в 4.5.1.0 и 5.7
                
                var gemIds = GemIds.Select(id => (long)id).ToArray();
                stream.WritePiscW(gemIds.Length, gemIds);
                //stream.WritePisc(GemIds[0], GemIds[1], GemIds[2], GemIds[3]);
                //stream.WritePisc(GemIds[4], GemIds[5], GemIds[6], GemIds[7]);
                //stream.WritePisc(GemIds[8], GemIds[9], GemIds[10], GemIds[11]);
                //stream.WritePisc(GemIds[12], GemIds[13], GemIds[14], GemIds[15]); // в 3+ длина данных 36 (когда нет информации), в 1.2 было 56
                //stream.WritePisc(GemIds[16], GemIds[17]);
                break;
            case ItemDetailType.Slave:
                mDetailLength = 34; // есть расшифровка в items/SummonSlave 30 in 3.5, 4.5.2.6, 34 in 4.5.1.0, 5.7
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
            case ItemDetailType.Unk12: // 12
                mDetailLength = 11; // 12 in 3.5, 4.5, 11 in 5.0
                break;
            case ItemDetailType.Unk13: // 13
                mDetailLength = 14; // added in 5.7, 4.5
                break;
            case ItemDetailType.Invalid:
            default:
                break;
        }
        mDetailLength -= 1;
        if (mDetailLength > 0)
        {
            Detail = new byte[mDetailLength];
            stream.Write(Detail);
        }
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

    }

    public virtual bool CanDestroy()
    {
        return true;
    }
}
