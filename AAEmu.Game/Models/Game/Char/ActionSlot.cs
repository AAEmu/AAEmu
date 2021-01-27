namespace AAEmu.Game.Models.Game.Char
{
    public enum ActionSlotType : byte
    {
        None = 0,
        Item = 1,
        Skill = 2,
        Unk3 = 3,
        Unk4 = 4,
        Unk5 = 5
    }

    public class ActionSlot
    {
        public ActionSlotType Type { get; set; }
        public uint ActionId { get; set; }
        public ulong ItemId { get; set; }
    }
}
