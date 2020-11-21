namespace AAEmu.Game.Models.Game.Formulas
{
    public enum FormulaOwnerType : byte
    {
        Character = 0,
        Npc = 1,
        Slave = 2,
        Housing = 3,
        Transfer = 4,
        Mate = 5,
        Shipyard = 6
    }

    public enum UnitFormulaKind : byte
    {
        // Commented Formula Kinds do not exist
        // TODO v.1.0
        MeleeCritical = 1,
        MeleeAntiMiss = 2,
        //MeleeDodge = 3,
        //MeleeBlock = 4,
        MeleeParry = 5,
        RangedCritical = 6,
        RangedAntiMiss = 7,
        //RangedDodge = 8,
        //RangedBlock = 9,
        SpellCritical = 10,
        SpellAntiMiss = 11,
        LevelDps = 12,
        LevelMana = 13,
        MaxHealth = 14,
        MaxMana = 15,
        HealthRegen = 16,
        ManaRegen = 17,
        Armor = 18,
        MagicResist = 19,
        Facet = 20,
        MeleeDpsInc = 21,
        Str = 22,
        Dex = 23,
        Sta = 24,
        Int = 25,
        Spi = 26,
        Fai = 27,
        RangedDpsInc = 28,
        SpellDpsInc = 29,
        CastingTolerance = 30,
        PersistentHealthRegen = 31,
        PersistentManaRegen = 32,
        BaseMissPercent = 33,
        KillExp = 34,
        MeleeCriticalBonus = 35,
        RangedCriticalBonus = 36,
        SpellCriticalBonus = 37,
        RangedParry = 38,

        // TODO v.1.2
        IncomingMeleeDamageAdd = 39,
        IncomingRangedDamageAdd = 40,
        IncomingSpellDamageAdd = 41,
        HealCritical = 42,
        HealDpsInc = 43,
        HealCriticalBonus = 44,
        Block = 45,
        Dodge = 46
    }

    public class UnitFormula : Formula
    {
        public UnitFormulaKind Kind { get; set; }
        public FormulaOwnerType Owner { get; set; }
    }
}
