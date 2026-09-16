using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Skills;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// The <c>unit_attribute_limits</c> table: the bounds the client keeps a unit attribute's composed
/// value inside. 10.0.2.13 ships 49 rows over 49 attributes; the other 200-odd attributes are
/// unbounded.
/// </summary>
/// <remarks>
/// Loaded nowhere before this: the values reach gameplay only through
/// <see cref="UnitAttributeLimitRules"/>, which <c>Unit.CalculateWithBonuses</c> calls and which the
/// hand-walked NPC, slave, mate, shipyard and transfer stat getters reach through
/// <c>Unit.ClampToLimit</c>, so a table with no rows loaded means "clamp nothing" rather than
/// "clamp to 0".
///
/// One row (id 8, <c>melee_block</c> 0..2000000000) names attribute 21, which the 10.0.2.13
/// <c>enum_unit_attribute</c> table does not carry. <see cref="UnitAttribute.MeleeBlock"/> keeps that
/// id for it.
/// </remarks>
[GameData]
public class UnitAttributeLimitGameData : Singleton<UnitAttributeLimitGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<UnitAttribute, UnitAttributeLimit> _limits;

    /// <summary>
    /// The bounds for an attribute, or null when the table has no row for it (the common case).
    /// </summary>
    public UnitAttributeLimit? GetLimit(UnitAttribute attribute) =>
        _limits != null && _limits.TryGetValue(attribute, out var limit) ? limit : null;

    public void Load(SqliteConnection connection)
    {
        var limits = new Dictionary<UnitAttribute, UnitAttributeLimit>();
        var attributeIds = new List<long>();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM unit_attribute_limits";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var attributeId = reader.GetInt64("unit_attribute_id", 0);
                attributeIds.Add(attributeId);
                if (attributeId is < 0 or > uint.MaxValue)
                    continue;
                limits[(UnitAttribute)(uint)attributeId] =
                    new UnitAttributeLimit(reader.GetInt64("minimum", 0), reader.GetInt64("maximum", 0));
            }
        }

        var unknownIds = UnitAttributeLoadRules.UnknownIds(attributeIds);
        if (unknownIds.Count > 0)
            Logger.Warn(UnitAttributeLoadRules.Warning("unit_attribute_limits", unknownIds));

        // Swapped in one assignment: a reader sees either no table or all of it.
        _limits = limits;
        Logger.Info("Loaded {0} unit attribute limits", limits.Count);
    }

    public void PostLoad()
    {
    }

    /// <summary>Test seam: forgets the loaded rows, so a later clamp reads no limits at all.</summary>
    public void ClearForTests() => _limits = null;
}
