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
        private uint _imageItemTemplateId;
        private bool _isDirty;

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

        public virtual byte DetailType => 0; // TODO 1.0 max type: 8, at 1.2 max type 9 (size: 9 bytes)

        // Helper
        public ItemContainer _holdingContainer { get; set; }
        public static uint Coins = 500;

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
        }

        public override PacketStream Write(PacketStream stream)
        {
            stream.Write(TemplateId);
            // TODO ...
            // if (TemplateId == 0)
            //     return stream;
            stream.Write(Id);
            stream.Write(Grade);
            stream.Write((byte)ItemFlags); //bounded
            stream.Write(Count);
            stream.Write(DetailType);
            WriteDetails(stream);
            stream.Write(CreateTime);
            stream.Write(LifespanMins);
            stream.Write(MadeUnitId);
            stream.Write(WorldId);
            stream.Write(UnsecureTime);
            stream.Write(UnpackTime);
            return stream;
        }

        public virtual void ReadDetails(PacketStream stream)
        {
        }

        public virtual void WriteDetails(PacketStream stream)
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
    }
}
