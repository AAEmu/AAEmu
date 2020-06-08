namespace AAEmu.Game.Models.Game.Items
{
    public enum EquipmentItemSlot : byte
    {
        Head = 0,
        Neck = 1,
        Chest = 2,
        Waist = 3,
        Legs = 4,
        Hands = 5,
        Feet = 6,
        Arms = 7,
        Back = 8,
        Ear1 = 9,
        Ear2 = 10,
        Finger1 = 11,
        Finger2 = 12,
        Undershirt = 13,
        Underpants = 14,
        Mainhand = 15,
        Offhand = 16,
        Ranged = 17,
        Musical = 18,
        Face = 19,
        Hair = 20,
        Glasses = 21,
        Reserved = 22,
        Tail = 23,
        Body = 24,
        Beard = 25,
        Backpack = 26,
        Cosplay = 27
    }

    public enum EquipmentItemSlotType : byte
    {
        Head = 1,
        Neck = 2,
        Chest = 3,
        Waist = 4,
        Legs = 5,
        Hands = 6,
        Feet = 7,
        Arms = 8,
        Back = 9,
        Ear = 10,
        Finger = 11,
        Undershirt = 12,
        Underpants = 13,
        Mainhand = 14,
        Offhand = 15,
        TwoHanded = 16,
        OneHanded = 17,
        Ranged = 18,
        Ammunition = 19,
        Shield = 20,
        Instrument = 21,
        Bag = 22,
        Face = 23,
        Hair = 24,
        Glasses = 25,
        Reserved = 26,
        Tail = 27,
        Body = 28,
        Beard = 29,
        Backpack = 30,
        Cosplay = 31
    }
    public enum WeaponTypeBuff : uint
    {
        Shield = 8226,
        TwoHanded = 8227,
        DuelWield = 4899,
        None = 0
    }

    public enum ArmorKindBuff : uint
    {
        Cloth4 = 713,
        Cloth7 = 714,
        Leather4 = 715,
        Leather7 = 716,
        Plate4 = 717,
        Plate7 = 740
    }
}
