﻿using System;

using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class EquipItem : Item
    {
        public override ItemDetailType DetailType => ItemDetailType.Equipment;

        //public byte Durability { get; set; } // see Item
        //public uint RuneId { get; set; }
        //public uint[] GemIds { get; set; }
        //public ushort TemperPhysical { get; set; }
        //public ushort TemperMagical { get; set; }

        public virtual int Str => 0;
        public virtual int Dex => 0;
        public virtual int Sta => 0;
        public virtual int Int => 0;
        public virtual int Spi => 0;
        public virtual byte MaxDurability => 0;

        public int RepairCost
        {
            get
            {
                var template = (EquipItemTemplate)Template;
                var grade = ItemManager.Instance.GetGradeTemplate(Grade);
                var cost = (ItemManager.Instance.GetDurabilityRepairCostFactor() * 0.0099999998f) * (1f - Durability * 1f / MaxDurability) * template.Price;
                cost = cost * grade.RefundMultiplier * 0.0099999998f;
                cost = (float)Math.Ceiling(cost);
                if (cost < 0 || cost < int.MinValue || cost > int.MaxValue)
                {
                    cost = 0;
                }

                return (int)cost;
            }
        }

        public EquipItem()
        {
            GemIds = new uint[16]; // 16 - 3.0.3.0, 7 - 1.2
        }

        public EquipItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
            GemIds = new uint[16]; // 16 - 3.0.3.0, 7 - 1.2
        }

        //public override void ReadDetails(PacketStream stream)
        //{
        //    Durability = stream.ReadByte();       // durability
        //    ChargeCount = stream.ReadInt16();     // chargeCount
        //    ChargeTime = stream.ReadDateTime();   // chargeTime
        //    TemperPhysical = stream.ReadUInt16(); // scaledA
        //    TemperMagical = stream.ReadUInt16();  // scaledB

        //    var mGems = stream.ReadPisc(4);
        //    GemIds[0] = (uint)mGems[0];
        //    GemIds[1] = (uint)mGems[1];
        //    GemIds[2] = (uint)mGems[2];
        //    GemIds[3] = (uint)mGems[3];

        //    mGems = stream.ReadPisc(4);
        //    GemIds[4] = (uint)mGems[0];
        //    GemIds[5] = (uint)mGems[1];
        //    GemIds[6] = (uint)mGems[2];
        //    GemIds[7] = (uint)mGems[3];

        //    mGems = stream.ReadPisc(4);
        //    GemIds[8] = (uint)mGems[0];
        //    GemIds[9] = (uint)mGems[1];
        //    GemIds[10] = (uint)mGems[2];
        //    GemIds[11] = (uint)mGems[3];

        //    mGems = stream.ReadPisc(4);
        //    GemIds[12] = (uint)mGems[0];
        //    GemIds[13] = (uint)mGems[1];
        //    GemIds[14] = (uint)mGems[2];
        //    GemIds[15] = (uint)mGems[3];
        //}

        //public override void WriteDetails(PacketStream stream)
        //{
        //    stream.Write(Durability);     // durability
        //    stream.Write(ChargeCount);    // chargeCount
        //    stream.Write(ChargeTime);     // chargeTime
        //    stream.Write(TemperPhysical); // scaledA
        //    stream.Write(TemperMagical);  // scaledB

        //    stream.WritePisc(GemIds[0], GemIds[1], GemIds[2], GemIds[3]);
        //    stream.WritePisc(GemIds[4], GemIds[5], GemIds[6], GemIds[7]);
        //    stream.WritePisc(GemIds[8], GemIds[9], GemIds[10], GemIds[11]);
        //    stream.WritePisc(GemIds[12], GemIds[13], GemIds[14], GemIds[15]);
        //}
    }
}
