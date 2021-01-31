using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Char;
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
        private int _lifespanMins;
        private uint _madeUnitId;
        private DateTime _createTime;
        private DateTime _unsecureTime;
        private DateTime _unpackTime;
        private DateTime _chargeUseSkillTime;
        private uint _imageItemTemplateId;
        private bool _isDirty;
        private byte _flags;
        private byte _durability;
        private short _chargeCount;
        private ushort _temperPhysical;
        private ushort _temperMagical;
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
        public int LifespanMins { get => _lifespanMins; set { _lifespanMins = value; _isDirty = true; } }
        public uint MadeUnitId { get => _madeUnitId; set { _madeUnitId = value; _isDirty = true; } }
        public DateTime CreateTime { get => _createTime; set { _createTime = value; _isDirty = true; } }
        public DateTime UnsecureTime { get => _unsecureTime; set { _unsecureTime = value; _isDirty = true; } }
        public DateTime UnpackTime { get => _unpackTime; set { _unpackTime = value; _isDirty = true; } }
        public uint ImageItemTemplateId { get => _imageItemTemplateId; set { _imageItemTemplateId = value; _isDirty = true; } }

        public virtual ItemDetailType DetailType { get; set; } // TODO 1.0 max type: 8, at 1.2 max type 9 (size: 9 bytes)
        public DateTime ChargeUseSkillTime { get => _chargeUseSkillTime; set { _chargeUseSkillTime = value; _isDirty = true; } } // added in 1.7
        public byte Flags { get => _flags; set { _flags = value; _isDirty = true; } }
        public byte Durability { get => _durability; set { _durability = value; _isDirty = true; } }
        public short ChargeCount { get => _chargeCount; set { _chargeCount = value; _isDirty = true; } }
        public DateTime ChargeTime { get => _chargeTime; set { _chargeTime = value; _isDirty = true; } }
        public ushort TemperPhysical { get => _temperPhysical; set { _temperPhysical = value; _isDirty = true; } }
        public ushort TemperMagical { get => _temperMagical; set { _temperMagical = value; _isDirty = true; } }
        public uint RuneId { get => _runeId; set { _runeId = value; _isDirty = true; } }

        public uint[] GemIds { get; set; }
        public byte[] Detail { get; set; }

        // Helper
        public ItemContainer HoldingContainer { get; set; }
        public static uint Coins = 500;
        public static uint TaxCertificate = 31891;
        public static uint BoundTaxCertificate = 31892;
        public static uint AppraisalCertificate = 28085;

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
            GemIds = new uint[7];
        }

        public Item(uint runeId)
        {
            RuneId = runeId;
            WorldId = AppConfiguration.Instance.Id;
            OwnerId = 0;
            Slot = -1;
            HoldingContainer = null;
            _isDirty = true;
            GemIds = new uint[7];
        }

        public Item(byte worldId, uint runeId)
        {
            WorldId = worldId;
            RuneId = runeId;
            OwnerId = 0;
            Slot = -1;
            HoldingContainer = null;
            _isDirty = true;
            GemIds = new uint[7];
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
            GemIds = new uint[7];
        }

        public Item(ulong id, ItemTemplate template, int count, uint runeId)
        {
            WorldId = AppConfiguration.Instance.Id;
            OwnerId = 0;
            Id = id;
            TemplateId = template.Id;
            Template = template;
            Count = count;
            RuneId = runeId;
            Slot = -1;
            HoldingContainer = null;
            _isDirty = true;
            GemIds = new uint[7];
        }

        public Item(byte worldId, ulong id, ItemTemplate template, int count, uint runeId)
        {
            WorldId = worldId;
            OwnerId = 0;
            Id = id;
            TemplateId = template.Id;
            Template = template;
            Count = count;
            RuneId = runeId;
            Slot = -1;
            HoldingContainer = null;
            _isDirty = true;
            GemIds = new uint[7];
        }

        public override void Read(PacketStream stream)
        {
            TemplateId = stream.ReadUInt32();
            if (TemplateId == 0)
                return;
            Id = stream.ReadUInt64();
            Grade = stream.ReadByte();
            Flags = stream.ReadByte();
            Count = stream.ReadInt32();

            DetailType = (ItemDetailType) stream.ReadByte();
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
            stream.Write(TemplateId);
            if (TemplateId == 0)
                return stream;
            stream.Write(Id);
            stream.Write(Grade);
            stream.Write((byte)ItemFlags); //bounded
            stream.Write(Count);

            stream.Write((byte)DetailType);
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
            int mDetailLength;
            switch ((byte)DetailType)
            {
                case 1:
                    ImageItemTemplateId = stream.ReadUInt32();
                    Durability = stream.ReadByte();
                    stream.ReadInt16();
                    RuneId = stream.ReadUInt32();

                    stream.ReadBytes(12);

                    for (var i = 0; i < GemIds.Length; i++)
                        GemIds[i] = stream.ReadUInt32();

                    TemperPhysical = stream.ReadUInt16();
                    TemperMagical = stream.ReadUInt16();
                    //mDetailLength = 56;
                    break;
                case 2:
                    mDetailLength = 30;
                    goto LABEL_11;
                case 3:
                    mDetailLength = 21;
                    goto LABEL_11;
                case 4:
                    mDetailLength = 10;
                    goto LABEL_11;
                case 5:
                case 11:
                    mDetailLength = 25;
                    goto LABEL_11;
                case 6:
                case 7:
                    mDetailLength = 17;
                    goto LABEL_11;
                case 8:
                    mDetailLength = 9;
                    goto LABEL_11;
                case 9:
                    mDetailLength = 5;
                    goto LABEL_11;
                case 10:
                    mDetailLength = 13;
LABEL_11:
                    mDetailLength -= 1;
                    Detail = stream.ReadBytes(mDetailLength);

                    break;
                default:
                    break;
            }
        }

        public virtual void WriteDetails(PacketStream stream)
        {
            int mDetailLength;
            switch ((byte)DetailType)
            {
                case 1:
                    stream.Write(ImageItemTemplateId);
                    stream.Write(Durability);
                    stream.Write((short)0);
                    stream.Write(RuneId);

                    stream.Write((uint)0);
                    stream.Write((uint)0);
                    stream.Write((uint)0);

                    foreach (var gemId in GemIds)
                        stream.Write(gemId);

                    stream.Write(TemperPhysical);
                    stream.Write(TemperMagical);
                    //mDetailLength = 56; // 56 - 1 = 55
                    break;
                case 2:
                    mDetailLength = 30;
                    goto LABEL_11;
                case 3:
                    mDetailLength = 21;
                    goto LABEL_11;
                case 4:
                    mDetailLength = 10;
                    goto LABEL_11;
                case 5:
                case 11:
                    mDetailLength = 25;
                    goto LABEL_11;
                case 6:
                case 7:
                    mDetailLength = 17;
                    goto LABEL_11;
                case 8:
                    mDetailLength = 9;
                    goto LABEL_11;
                case 9:
                    mDetailLength = 5;
                    goto LABEL_11;
                case 10:
                    mDetailLength = 13;
LABEL_11:
                    mDetailLength -= 1;
                    if (mDetailLength > 0)
                    {
                        //Detail = new byte[mDetailLength];
                        stream.Write(Detail);
                    }

                    break;
                default:
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
