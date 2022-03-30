using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    [Flags]
    public enum ItemFlag : byte
    {
        None = 0x00,
        SoulBound = 0x01,
        HasUCC = 0x02,
        Secure = 0x04,
        Skinized = 0x08,
        Unpacked = 0x10,
        AuctionWin = 0x20
    }

    public enum ShopCurrencyType : byte
    {
        Money = 0,
        Honor = 1,
        VocationBadges = 2,
        SiegeShop = 3,
    }

    public struct ItemLocation
    {
        public SlotType slotType;
        public byte Slot;
    }

    public struct ItemIdAndLocation
    {
        public ulong Id;
        public SlotType SlotType;
        public byte Slot;
    }
    
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
        private uint _lifespanMins;
        private uint _madeUnitId;
        private DateTime _createTime;
        private DateTime _unsecureTime;
        private DateTime _unpackTime;
        private uint _imageItemTemplateId;
        private bool _isDirty;
        private ulong _uccId;
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
        public SlotType SlotType { get => _slotType; set { _slotType = value; _isDirty = true; } }
        public int Slot { get => _slot; set { _slot = value; _isDirty = true; } }
        public byte Grade { get => _grade; set { _grade = value; _isDirty = true; } }
        public ItemFlag ItemFlags { get => _itemFlags; set { _itemFlags = value; _isDirty = true; } }
        public int Count { get => _count; set { _count = value; _isDirty = true; } }
        public uint LifespanMins { get => _lifespanMins; set { _lifespanMins = value; _isDirty = true; } }
        public uint MadeUnitId { get => _madeUnitId; set { _madeUnitId = value; _isDirty = true; } }
        public DateTime CreateTime { get => _createTime; set { _createTime = value; _isDirty = true; } }
        public DateTime UnsecureTime { get => _unsecureTime; set { _unsecureTime = value; _isDirty = true; } }
        public DateTime UnpackTime { get => _unpackTime; set { _unpackTime = value; _isDirty = true; } }
        public uint ImageItemTemplateId { get => _imageItemTemplateId; set { _imageItemTemplateId = value; _isDirty = true; } }

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

        public virtual ItemDetailType DetailType { get; set; } // TODO 1.0 max type: 8, at 1.2 max type 9, at 3.0.3.0 max type 10, at 3.5.0.3 max type 12, at 5.7 max type 13
        public DateTime ChargeUseSkillTime { get => _chargeUseSkillTime; set { _chargeUseSkillTime = value; _isDirty = true; } }
        public byte Flags { get => _flags; set { _flags = value; _isDirty = true; } }
        public byte Durability { get => _durability; set { _durability = value; _isDirty = true; } }
        public short ChargeCount { get => _chargeCount; set { _chargeCount = value; _isDirty = true; } }
        public DateTime ChargeTime { get => _chargeTime; set { _chargeTime = value; _isDirty = true; } }
        public ushort TemperPhysical { get => _TemperPhysical; set { _TemperPhysical = value; _isDirty = true; } }
        public ushort TemperMagical { get => _TemperMagical; set { _TemperMagical = value; _isDirty = true; } }
        public uint RuneId { get => _runeId; set { _runeId = value; _isDirty = true; } }

        public ushort ScaledA { get; set; }
        public ushort ScaledB { get; set; }
        public ushort EvolveChance { get; set; }
        public DateTime ChargeProcTime { get; set; }
        public byte MappingFailBonus { get; set; }
        public byte ElementLevel { get; set; }

        public uint[] GemIds { get; set; } // size 16 in 1.2, 18 in 8+
        public byte[] Detail { get; set; }

        // Helper
        public ItemContainer _holdingContainer { get; set; }
        public static uint Coins = 500;
        public static uint TaxCertificate = 31891;
        public static uint BoundTaxCertificate = 31892;
        public static uint AppraisalCertificate = 28085;
        public static uint CrestStamp = 17662;
        public static uint CrestInk = 17663;
        public static uint SheetMusic = 28051;
        public static uint SalonCertificate = 30811;

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
            if (TemplateId != 0)
            {
                Id = stream.ReadUInt64();
                Grade = stream.ReadByte();
                Flags = stream.ReadByte();
                Count = stream.ReadInt32();

                DetailType = (ItemDetailType)stream.ReadByte();
                ReadDetails(stream);

                CreateTime = stream.ReadDateTime();
                LifespanMins = stream.ReadUInt32();
                MadeUnitId = stream.ReadUInt32();
                WorldId = stream.ReadByte();
                UnsecureTime = stream.ReadDateTime();
                UnpackTime = stream.ReadDateTime();
                ChargeUseSkillTime = stream.ReadDateTime();
            }
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(TemplateId);
            if (TemplateId != 0)
            {
                stream.Write(Id);
                stream.Write(Grade);
                stream.Write(Flags);
                stream.Write(Count);

                stream.Write((byte)DetailType);
                WriteDetails(stream);

                stream.Write(CreateTime);
                stream.Write(LifespanMins);
                stream.Write(MadeUnitId);
                stream.Write(WorldId);
                stream.Write(UnsecureTime);
                stream.Write(UnpackTime);
                stream.Write(ChargeUseSkillTime);
            }
            return stream;
        }

        public virtual void ReadDetails(PacketStream stream)
        {
            int mDetailLength;
            switch (DetailType)
            {
                case ItemDetailType.Equipment:
                    Durability = stream.ReadByte();
                    ChargeCount = stream.ReadInt16();
                    ChargeTime = stream.ReadDateTime();
                    ScaledA = stream.ReadUInt16();          // type
                    EvolveChance = stream.ReadUInt16();     // evolveChance
                    ChargeProcTime = stream.ReadDateTime(); // chargeProcTime
                    MappingFailBonus = stream.ReadByte();   // mappingFailBonus
                    ElementLevel = stream.ReadByte();       // elementLevel in 6.5.3.3

                    var mGems = stream.ReadPisc(4);
                    GemIds[0] = (uint)mGems[0];
                    GemIds[1] = (uint)mGems[1];
                    GemIds[2] = (uint)mGems[2];
                    GemIds[3] = (uint)mGems[3];
                    mGems = stream.ReadPisc(4); // 14
                    GemIds[4] = (uint)mGems[0];
                    GemIds[5] = (uint)mGems[1];
                    GemIds[6] = (uint)mGems[2];
                    GemIds[7] = (uint)mGems[3];
                    mGems = stream.ReadPisc(4);
                    GemIds[8] = (uint)mGems[0];
                    GemIds[9] = (uint)mGems[1];
                    GemIds[10] = (uint)mGems[2];
                    GemIds[11] = (uint)mGems[3];
                    mGems = stream.ReadPisc(4);
                    GemIds[12] = (uint)mGems[0];
                    GemIds[13] = (uint)mGems[1];
                    GemIds[14] = (uint)mGems[2];
                    GemIds[15] = (uint)mGems[3];
                    mGems = stream.ReadPisc(2);
                    GemIds[16] = (uint)mGems[0];
                    GemIds[17] = (uint)mGems[1];
                    break;
                case ItemDetailType.Slave:
                    mDetailLength = 34; // 30 in 3.5, 34 in 5.7
                    goto Label_25;
                case ItemDetailType.Mate:
                    mDetailLength = 21;
                    goto Label_25;
                case ItemDetailType.Ucc:
                    mDetailLength = 10;
                    goto Label_25;
                case ItemDetailType.Treasure:
                case ItemDetailType.Location:
                    mDetailLength = 25;
                    goto Label_25;
                case ItemDetailType.BigFish:
                case ItemDetailType.Decoration:
                    mDetailLength = 17;
                    goto Label_25;
                case ItemDetailType.MusicSheet:
                    mDetailLength = 9;
                    goto Label_25;
                case ItemDetailType.Glider:
                    mDetailLength = 5;
                    goto Label_25;
                case ItemDetailType.SlaveEquipment:
                    mDetailLength = 13;
                    goto Label_25;
                case ItemDetailType.Unk12:
                    mDetailLength = 11; // 12 in 3.5, 11 in 5.7
                    goto Label_25;
                case ItemDetailType.Unk13:
                    mDetailLength = 14; // added in 5.7
Label_25:
                    mDetailLength -= 1;
                    if (mDetailLength > 0)
                    {
                        Detail = stream.ReadBytes(mDetailLength);
                    }

                    break;
                case ItemDetailType.Invalid:
                    break;
            }
        }

        public virtual void WriteDetails(PacketStream stream)
        {
            var mDetailLength = 0;
            switch (DetailType)
            {
                case ItemDetailType.Equipment:
                    stream.Write(Durability);       // durability
                    stream.Write(ChargeCount);      // chargeCount
                    stream.Write(ChargeTime);       // chargeTime
                    stream.Write(ScaledA);          // scaledA
                    stream.Write(EvolveChance);     // evolveChance
                    stream.Write(ChargeProcTime);   // chargeProcTime
                    stream.Write(MappingFailBonus); // mappingFailBonus
                    stream.Write(ElementLevel);     // elementLevel in 6.5.3.3

                    stream.WritePisc(GemIds[0], GemIds[1], GemIds[2], GemIds[3]);
                    stream.WritePisc(GemIds[4], GemIds[5], GemIds[6], GemIds[7]);
                    stream.WritePisc(GemIds[8], GemIds[9], GemIds[10], GemIds[11]);
                    stream.WritePisc(GemIds[12], GemIds[13], GemIds[14], GemIds[15]);
                    stream.WritePisc(GemIds[16], GemIds[17]);
                    break;
                case ItemDetailType.Slave:
                    mDetailLength = 34; // 30 in 3.5, 34 in 5.7+
                    goto Label_25;
                case ItemDetailType.Mate:
                    mDetailLength = 21;
                    goto Label_25;
                case ItemDetailType.Ucc:
                    mDetailLength = 10;
                    goto Label_25;
                case ItemDetailType.Treasure:
                case ItemDetailType.Location:
                    mDetailLength = 25;
                    goto Label_25;
                case ItemDetailType.BigFish:
                case ItemDetailType.Decoration:
                    mDetailLength = 17;
                    goto Label_25;
                case ItemDetailType.MusicSheet:
                    mDetailLength = 9;
                    goto Label_25;
                case ItemDetailType.Glider:
                    mDetailLength = 5;
                    goto Label_25;
                case ItemDetailType.SlaveEquipment:
                    mDetailLength = 13;
                    goto Label_25;
                case ItemDetailType.Unk12:
                    mDetailLength = 11; // 12 in 3.5, 11 in 5.7+
                    goto Label_25;
                case ItemDetailType.Unk13:
                    mDetailLength = 14; // added in 5.7+
Label_25:
                    mDetailLength -= 1;
                    if (mDetailLength > 0)
                    {
                        stream.Write(Detail);
                    }

                    break;
                case ItemDetailType.Invalid:
                    break;
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
    }
}
