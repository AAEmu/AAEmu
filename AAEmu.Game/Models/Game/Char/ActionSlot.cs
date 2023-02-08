namespace AAEmu.Game.Models.Game.Char
{
    public enum ActionSlotType : byte
    {
        None = 0,
        ItemType = 1,
        Spell = 2,
        Macro = 3,
        ItemId = 4,
        RidePetSpell = 5
    }

    public class ActionSlot
    {
        public ActionSlotType Type { get; set; }
        public ulong ActionId { get; set; }
    }
}
