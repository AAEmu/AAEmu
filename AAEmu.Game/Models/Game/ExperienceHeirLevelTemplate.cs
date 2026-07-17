namespace AAEmu.Game.Models.Game;

/// <summary>
/// Шаблон наследственного уровня из таблицы <c>heir_levels</c> БД <c>compact.sqlite3</c>.
/// </summary>
/// <remarks>
/// Схема таблицы <c>heir_levels</c>:
/// <list type="bullet">
///   <item><description><c>id</c> int PRIMARY KEY → <see cref="Id"/></description></item>
///   <item><description><c>level</c> int → <see cref="Level"/></description></item>
///   <item><description><c>req_item_count</c> int → <see cref="ReqItemCount"/></description></item>
///   <item><description><c>req_item_id</c> int → <see cref="ReqItemId"/></description></item>
///   <item><description><c>req_total_exp</c> int → <see cref="ReqTotalExp"/></description></item>
///   <item><description><c>step</c> int → <see cref="Step"/></description></item>
/// </list>
/// </remarks>
public class ExperienceHeirLevelTemplate
{
    public int Id { get; set; }
    public byte Level { get; set; }
    public int ReqItemCount { get; set; }
    public int ReqItemId { get; set; }
    public uint ReqTotalExp { get; set; }
    public int Step { get; set; }
}
