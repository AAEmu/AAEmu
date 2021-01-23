using System;

namespace AAEmu.Game.Models.Game.Items.Templates
{
    public class BodyPartTemplate : ItemTemplate
    {
        public override Type ClassType => typeof(BodyPart);

        public uint ModelId { get; set; }
        public bool NpcOnly { get; set; }
        public bool BeautyShopOnly { get; set; }
        public uint ItemId { get; set; }
        public uint SlotTypeId { get; set; }
    }
}
