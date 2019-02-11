using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class ItemTemplate
    {
        public virtual Type ClassType => typeof(Item);

        public uint Id { get; set; }
        public int Level { get; set; }
        public int Price { get; set; }
        public int Refund { get; set; }
        public uint BindId { get; set; }
        public int PickupLimit { get; set; }
        public int MaxCount { get; set; }
        public bool Sellable { get; set; }
        public uint UseSkillId { get; set; }
        public uint BuffId { get; set; }
        public bool Gradable { get; set; }
        public bool LootMulti { get; set; }
        public uint LootQuestId { get; set; }
        public int HonorPrice { get; set; }
        public int ExpAbsLifetime { get; set; }
        public int ExpOnlineLifetime { get; set; }
        public int ExpDate { get; set; }
        public int LevelRequirement { get; set; }
        public int LevelLimit { get; set; }
        public int FixedGrade { get; set; }
        public int LivingPointPrice { get; set; }
        public byte CharGender { get; set; }
    }
}
