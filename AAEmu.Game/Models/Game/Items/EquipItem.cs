using System;
using AAEmu.Commons.Network;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Items.Templates;

namespace AAEmu.Game.Models.Game.Items
{
    public class EquipItem : Item
    {
        public override ItemDetailType DetailType => ItemDetailType.Equipment;

        public byte Durability { get; set; }
        public uint RuneId { get; set; }
        public uint[] GemIds { get; set; }
        public ushort TemperPhysical { get; set; }
        public ushort TemperMagical { get; set; }

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
                var cost = (ItemManager.Instance.GetDurabilityRepairCostFactor() * 0.0099999998f) *
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
            GemIds = new uint[7];
        }

        public EquipItem(ulong id, ItemTemplate template, int count) : base(id, template, count)
        {
            GemIds = new uint[7];
        }

        public override void ReadDetails(PacketStream stream)
        {
            ImageItemTemplateId = stream.ReadUInt32();
            Durability = stream.ReadByte();
            stream.ReadInt16();
            RuneId = stream.ReadUInt32();

            stream.ReadBytes(12);

            for (var i = 0; i < GemIds.Length; i++)
                GemIds[i] = stream.ReadUInt32();

            TemperPhysical = stream.ReadUInt16();
            TemperMagical = stream.ReadUInt16();
        }

        public override void WriteDetails(PacketStream stream)
        {
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
        }
    }
}
