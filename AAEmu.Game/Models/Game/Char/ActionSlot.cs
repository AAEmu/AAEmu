namespace AAEmu.Game.Models.Game.Char
{
    public enum ActionSlotType : byte
    {
        None = 0,
        ItemType = 1,
        Spell = 2,
        Macro = 3,
        ItemId = 4,
        RidePetSpell = 5,
        BattlePetSpell = 6
    }

    public class ActionSlot  // 85 in 1.2, 121 in 3.0.3.0, 133 in 3.5.0.3
    {
        public ActionSlotType Type { get; set; }
        public ulong ActionId { get; set; }
    }
}
