namespace AAEmu.Game.Models.Game.Formulas;

public enum FormulaOwnerType : byte
{
    Character = 0,
    Npc = 1,
    Slave = 2,
    Housing = 3,
    Transfer = 4,
    Mate = 5,
    Shipyard = 6,

    /// <summary>
    /// enum_unit_owner_types' eighth row. unit_formulas carries 60 butler rows (owner_type_id 7) that the
    /// loader dropped while the enum stopped at 6; <c>FormulaManager.Load</c> seeds its table from the enum,
    /// so declaring the row is what starts loading them.
    /// </summary>
    Butler = 7
}
