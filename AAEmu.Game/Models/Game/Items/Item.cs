﻿using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Models.Game.Items.Containers;
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
        private ulong _uccId;
        private DateTime _expirationTime;
        private double _expirationOnlineMinutesLeft;

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
        public int ChargeCount { get; set; }
        
        public virtual ItemDetailType DetailType => 0; // TODO 1.0 max type: 8, at 1.2 max type 9 (size: 9 bytes)
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
            stream.Write((byte)DetailType);
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
            int mDetailLength;
            switch ((byte)DetailType)
            {
                case 1: // Equipment // есть расшифровка в items/Equipment
                    mDetailLength = 56;
                    goto Label_32;
                case 2: // Slave
                    mDetailLength = 30;
                    goto Label_32;
                case 3: // Mate
                    mDetailLength = 7; // есть расшифровка в items/Summon
                    goto Label_32;
                case 4: // Ucc
                    mDetailLength = 10; // есть расшифровка в items/UccItem
                    goto Label_32;
                case 5:  // Treasure
                case 11: // Location
                    mDetailLength = 25;
                    goto Label_32;
                case 6: // BigFish
                case 7: // Decoration
                    mDetailLength = 17;
                    goto Label_32;
                case 8: // MusicSheet
                    mDetailLength = 9; // есть расшифровка в items/MusicSheetItem
                    goto Label_32;
                case 9: // Glider
                    mDetailLength = 5;
                    goto Label_32;
                case 10: // SlaveEquipment
                    mDetailLength = 13;
Label_32:
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
            switch (DetailType)
            {
                case ItemDetailType.Equipment:
                    mDetailLength = 56; // есть расшифровка в items/Equipment
                    goto Label_32;
                case ItemDetailType.Slave:
                    mDetailLength = 30;
                    goto Label_32;
                case ItemDetailType.Mate:
                    mDetailLength = 7; // есть расшифровка в items/Summon
                    goto Label_32;
                case ItemDetailType.Ucc:
                    mDetailLength = 10; // есть расшифровка в items/UccItem
                    goto Label_32;
                case ItemDetailType.Treasure:
                //case ItemDetailType.Location: // нет в 1.2
                    mDetailLength = 25;
                    goto Label_32;
                case ItemDetailType.BigFish:
                case ItemDetailType.Decoration:
                    mDetailLength = 17;
                    goto Label_32;
                case ItemDetailType.MusicSheet:
                    mDetailLength = 9; // есть расшифровка в items/MusicSheetItem
                    goto Label_32;
                case ItemDetailType.Glider:
                    mDetailLength = 5;
                //    goto Label_32;
                //case ItemDetailType.SlaveEquipment: // нет в 1.2
                //    mDetailLength = 13;
Label_32:
                    mDetailLength -= 1;
                    Detail = new byte[mDetailLength];
                    stream.Write(Detail);
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
