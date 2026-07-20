using AAEmu.Game.Models.StaticValues;

namespace AAEmu.Game.Models.Game.Faction;

/// <summary>
/// Отношение между системными фракциями из таблицы <c>system_faction_relations</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>system_faction_relations</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>faction1_id</c> int → <see cref="Faction1Id"/></description></item>
///   <item><description><c>faction2_id</c> int → <see cref="Faction2Id"/></description></item>
///   <item><description><c>state_id</c> int → <see cref="State"/></description></item>
/// </list>
/// </remarks>
public class FactionRelation
{
    public uint Id { get; set; }
    public FactionsEnum Faction1Id { get; set; }
    public FactionsEnum Faction2Id { get; set; }
    public RelationState State { get; set; }
}
