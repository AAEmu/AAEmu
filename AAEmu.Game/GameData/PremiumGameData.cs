using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Premium;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

namespace AAEmu.Game.GameData;

/// <summary>
/// premium_grades and premium_configs — the point thresholds behind a character's premium grade.
/// </summary>
/// <remarks>
/// UnitState carries the grade, and it used to be written as a hardcoded 1. Grade 1 is the free tier
/// (threshold 0, two online labor, no buff), so the constant was not granting anything — it pinned
/// every character there, and the five paid grades above it could never show however many points the
/// account held. The grade is a lookup: characters.point is already loaded into Character.Point, and
/// the grade is the highest row whose threshold that total reaches (1, 50, 125, 225 and 400).
/// </remarks>
[GameData]
public class PremiumGameData : Singleton<PremiumGameData>, IGameDataLoader
{
    private List<PremiumGrade> _grades = [];
    private PremiumConfig _config;

    /// <summary>
    /// Highest grade whose threshold <paramref name="point"/> reaches, or 0 for no premium.
    /// </summary>
    public uint GetGradeForPoint(int point)
    {
        uint grade = 0;
        foreach (var g in _grades)
        {
            if (point < g.Point)
                continue;
            if (g.GradeId > grade)
                grade = g.GradeId;
        }

        return grade;
    }

    public PremiumGrade GetGrade(uint gradeId) => _grades.FirstOrDefault(g => g.GradeId == gradeId);

    /// <summary>
    /// The account-wide grade lives in <c>AccountManager.GetAccountPremium</c>, which can reach the
    /// database for the paths that run before the character list is loaded. This class stays a pure
    /// lookup over the client data.
    /// </summary>

    /// <summary>
    /// Highest grade premium_grades defines (6 on 10.0.2.13), or 0 when the table is empty. Used by
    /// <see cref="Models.Game.Configurations.AccountConfig.ForceMaxPremiumGrade"/> rather than a
    /// constant, so it follows the client data instead of going stale against it.
    /// </summary>
    public uint MaxGradeId => _grades.Count == 0 ? 0u : _grades.Max(g => g.GradeId);

    /// <summary>
    /// Every buff premium_grades attaches to a grade. Used to strip the buff of a grade a character no
    /// longer holds before granting the current one. The free tier carries none, hence the filter.
    /// </summary>
    public IEnumerable<uint> GradeBuffIds => _grades.Where(g => g.BuffId > 0).Select(g => g.BuffId).Distinct();

    public PremiumConfig Config => _config;

    public void Load(SqliteConnection connection)
    {
        _grades = [];
        _config = null;

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM premium_grades";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                _grades.Add(new PremiumGrade
                {
                    Id = reader.GetUInt32("id"),
                    GradeId = reader.GetUInt32("grade_id"),
                    Point = reader.GetInt32("point", 0),
                    BuffId = reader.GetUInt32("buff_id", 0),
                    OnlineLabor = reader.GetInt32("online_labor", 0),
                    OfflineLabor = reader.GetInt32("offline_labor", 0),
                    MaxLabor = reader.GetInt32("max_labor", 0),
                    MaxLocalLabor = reader.GetInt32("max_local_labor", 0),
                    ConfigId = reader.GetUInt32("config_id", 0)
                });
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM premium_configs";
            command.Prepare();
            using var reader = new SQLiteWrapperReader(command.ExecuteReader());
            while (reader.Read())
            {
                _config = new PremiumConfig
                {
                    Id = reader.GetUInt32("id"),
                    ConnectPoint = reader.GetInt32("connect_point", 0),
                    DisconnectPoint = reader.GetInt32("disconnect_point", 0),
                    DeactivatePoint = reader.GetInt32("deactivate_point", 0),
                    MaxPoint = reader.GetInt32("max_point", 0),
                    AuctionChargeDiscount = reader.GetInt32("auction_charge_discount", 0),
                    AuctionDepositDiscount = reader.GetInt32("auction_deposit_discount", 0),
                    MaxGradeId = reader.GetUInt32("max_grade_id", 0)
                };
            }
        }
    }

    public void PostLoad()
    {
        // Nothing to do here
    }
}
