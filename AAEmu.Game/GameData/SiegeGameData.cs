using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Dominions;
using AAEmu.Game.Models.Game.Sieges;
using AAEmu.Game.Models.Game.Skills.Effects;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;

using NLog;

namespace AAEmu.Game.GameData;

/// <summary>
/// One row of <c>guard_tower_settings</c> — the real source for a claimed Dominion's territory radii/gate-and-wall
/// caps, keyed by <see cref="Models.Game.Housing.HousingTemplate.GuardTowerSettingId"/> on the claimed lodestone's
/// housing template. Previously hardcoded per-claim in <c>DeclareDominion</c>.
/// </summary>
public class GuardTowerSettings
{
    public uint Id { get; init; }
    public uint InitialBuffId { get; init; }
    public byte MaxGates { get; init; }
    public byte MaxWalls { get; init; }
    public short RadiusDeclare { get; init; }
    public ushort RadiusDominion { get; init; }
    public short RadiusOffenseHq { get; init; }
    public short RadiusSiege { get; init; }
}

/// <summary>
/// One row of <c>siege_zones</c> — the recurring weekly schedule template for a zone group's siege cycle. All
/// "_weekday" fields are a day offset added to <see cref="SiegePlan.WeekStart"/>. Every shipped row uses
/// the same offset against a Tuesday week_start, which is the reading that matches the four siege_zones
/// windows.
/// </summary>
public class SiegeZoneSchedule
{
    /// <summary>
    /// The <c>siege_zones</c> row id — the raid zone the client names in its own list of the four siege
    /// territories (Heedmar, Nuimari, Marcala, Calmlands), which is what the raid team list identifies a team's
    /// zone by. Distinct from <see cref="ZoneGroupId"/>, which is what our roster and the Dominion claim are
    /// keyed by.
    /// </summary>
    public uint Id { get; init; }

    public uint ZoneGroupId { get; init; }
    public int ReinforceDefenseDelayMins { get; init; }
    public uint DefenseMerchantId { get; init; }
    public uint OffenseMerchantId { get; init; }
    public uint DominionMerchantId { get; init; }
    public uint MonumentDoodadId { get; init; }

    public int StartHeroVolunteerWeekdayOffset { get; init; }
    public int StartHeroVolunteerHour { get; init; }
    public int StartHeroVolunteerMin { get; init; }

    public int StartReadyToSiegeWeekdayOffset { get; init; }
    public int StartReadyToSiegeHour { get; init; }
    public int StartReadyToSiegeMin { get; init; }

    public int StartDeclareDominionWeekdayOffset { get; init; }
    public int StartDeclareDominionHour { get; init; }
    public int StartDeclareDominionMin { get; init; }
    public TimeSpan DeclareDominionDuration { get; init; }

    public int StartSiegeWeekdayOffset { get; init; }
    public int StartSiegeHour { get; init; }
    public int StartSiegeMin { get; init; }
    public TimeSpan SiegeDuration { get; init; }

    private static DateTime At(DateTime weekStart, int dayOffset, int hour, int min) =>
        weekStart.AddDays(dayOffset).Date.AddHours(hour).AddMinutes(min);

    public DateTime HeroVolunteerStart(DateTime weekStart) =>
        At(weekStart, StartHeroVolunteerWeekdayOffset, StartHeroVolunteerHour, StartHeroVolunteerMin);

    public DateTime ReadyToSiegeStart(DateTime weekStart) =>
        At(weekStart, StartReadyToSiegeWeekdayOffset, StartReadyToSiegeHour, StartReadyToSiegeMin);

    public DateTime DeclareDominionStart(DateTime weekStart) =>
        At(weekStart, StartDeclareDominionWeekdayOffset, StartDeclareDominionHour, StartDeclareDominionMin);

    public DateTime DeclareDominionEnd(DateTime weekStart) => DeclareDominionStart(weekStart) + DeclareDominionDuration;

    public DateTime SiegeStart(DateTime weekStart) =>
        At(weekStart, StartSiegeWeekdayOffset, StartSiegeHour, StartSiegeMin);

    public DateTime SiegeEnd(DateTime weekStart) => SiegeStart(weekStart) + SiegeDuration;
}

/// <summary>One concrete weekly siege-cycle instance from <c>siege_plans</c>: "zone group X has a cycle starting at week_start Y".</summary>
public readonly record struct SiegePlan(uint ZoneGroupId, DateTime WeekStart);

/// <summary>
/// One row of <c>guard_tower_steps</c> — cap and buff for a claimed lodestone, keyed by
/// (<c>guard_tower_setting_id</c>, step). Distinct from per-house <c>housing_build_steps</c>
/// (gates/walls/buff, not a model). Lodestones have no <c>housing_build_steps</c> rows, so
/// <see cref="Models.Game.Housing.House.CurrentStep"/> starts at -1. The row is a cap, not a
/// spawn list; walls and gates come from player drawings, not this table.
/// </summary>
public class GuardTowerStep
{
    public uint GuardTowerSettingId { get; init; }
    public int Step { get; init; }
    public byte NumGates { get; init; }
    public byte NumWalls { get; init; }
    public uint BuffId { get; init; }
}

/// <summary>The <c>guard_tower_settings</c>/<c>siege_zones</c>/<c>siege_plans</c> template tables — see the individual row types.</summary>
[GameData]
public class SiegeGameData : Singleton<SiegeGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, GuardTowerSettings> _guardTowerSettings = [];
    private Dictionary<uint, SiegeZoneSchedule> _siegeZoneSchedules = [];
    private Dictionary<uint, List<DateTime>> _siegePlanWeekStartsByZoneGroup = [];
    private Dictionary<uint, List<GuardTowerStep>> _guardTowerStepsBySettingId = [];
    private readonly HashSet<uint> _uniqueDominionHousingDesigns = [];
    private readonly HashSet<uint> _lodestoneTemplateIds = [];

    public void Load(SqliteConnection connection)
    {
        _guardTowerSettings = [];
        _siegeZoneSchedules = [];
        _siegePlanWeekStartsByZoneGroup = [];
        _guardTowerStepsBySettingId = [];
        _uniqueDominionHousingDesigns.Clear();
        _lodestoneTemplateIds.Clear();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM guard_tower_settings";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var settings = new GuardTowerSettings
                {
                    Id = reader.GetUInt32("id"),
                    InitialBuffId = reader.GetUInt32("initial_buff_id", 0),
                    MaxGates = (byte)reader.GetInt32("max_gates", 0),
                    MaxWalls = (byte)reader.GetInt32("max_walls", 0),
                    RadiusDeclare = (short)reader.GetInt32("radius_declare", 0),
                    RadiusDominion = (ushort)reader.GetInt32("radius_dominion", 0),
                    RadiusOffenseHq = (short)reader.GetInt32("radius_offense_hq", 0),
                    RadiusSiege = (short)reader.GetInt32("radius_siege", 0)
                };

                _guardTowerSettings[settings.Id] = settings;
            }
        }

        Logger.Info("Loaded {0} guard tower settings", _guardTowerSettings.Count);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM guard_tower_steps ORDER BY guard_tower_setting_id, step";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var step = new GuardTowerStep
                {
                    GuardTowerSettingId = reader.GetUInt32("guard_tower_setting_id"),
                    Step = reader.GetInt32("step", 0),
                    NumGates = (byte)reader.GetInt32("num_gates", 0),
                    NumWalls = (byte)reader.GetInt32("num_walls", 0),
                    BuffId = reader.GetUInt32("buff_id", 0)
                };

                if (!_guardTowerStepsBySettingId.TryGetValue(step.GuardTowerSettingId, out var list))
                    _guardTowerStepsBySettingId[step.GuardTowerSettingId] = list = [];
                list.Add(step);
            }
        }

        Logger.Info("Loaded guard tower step progressions for {0} settings", _guardTowerStepsBySettingId.Count);

        using (var command = connection.CreateCommand())
        {
            // One housing_id per unique territory building (altar / farm / workshop / warehouse / overseer).
            // Walls, gates, and towers have no row here and may be placed more than once.
            command.CommandText = "SELECT DISTINCT housing_id FROM dominion_housings";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
                _uniqueDominionHousingDesigns.Add(reader.GetUInt32("housing_id"));
        }

        Logger.Info("Loaded {0} unique-per-territory dominion housing designs", _uniqueDominionHousingDesigns.Count);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT * FROM siege_zones";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var schedule = new SiegeZoneSchedule
                {
                    Id = reader.GetUInt32("id"),
                    ZoneGroupId = reader.GetUInt32("zone_group_id"),
                    ReinforceDefenseDelayMins = reader.GetInt32("reinforce_defense_delay_mins", 0),
                    DefenseMerchantId = reader.GetUInt32("defense_merchant_id", 0),
                    OffenseMerchantId = reader.GetUInt32("offense_merchant_id", 0),
                    DominionMerchantId = reader.GetUInt32("dominion_merchant_id", 0),
                    MonumentDoodadId = reader.GetUInt32("monument_doodad_id", 0),

                    StartHeroVolunteerWeekdayOffset = reader.GetInt32("start_hero_volunteer_weekday", 0),
                    StartHeroVolunteerHour = reader.GetInt32("start_hero_volunteer_hour", 0),
                    StartHeroVolunteerMin = reader.GetInt32("start_hero_volunteer_min", 0),

                    StartReadyToSiegeWeekdayOffset = reader.GetInt32("start_ready_to_siege_weekday", 0),
                    StartReadyToSiegeHour = reader.GetInt32("start_ready_to_siege_hour", 0),
                    StartReadyToSiegeMin = reader.GetInt32("start_ready_to_siege_min", 0),

                    StartDeclareDominionWeekdayOffset = reader.GetInt32("start_declare_dominion_weekday", 0),
                    StartDeclareDominionHour = reader.GetInt32("start_declare_dominion_hour", 0),
                    StartDeclareDominionMin = reader.GetInt32("start_declare_dominion_min", 0),
                    DeclareDominionDuration = new TimeSpan(
                        reader.GetInt32("declare_dominion_days", 0),
                        reader.GetInt32("declare_dominion_hours", 0),
                        reader.GetInt32("declare_dominion_mins", 0), 0),

                    StartSiegeWeekdayOffset = reader.GetInt32("start_siege_weekday", 0),
                    StartSiegeHour = reader.GetInt32("start_siege_hour", 0),
                    StartSiegeMin = reader.GetInt32("start_siege_min", 0),
                    SiegeDuration = new TimeSpan(
                        reader.GetInt32("siege_days", 0),
                        reader.GetInt32("siege_hours", 0),
                        reader.GetInt32("siege_mins", 0), 0)
                };

                _siegeZoneSchedules[schedule.ZoneGroupId] = schedule;
            }
        }

        Logger.Info("Loaded {0} siege zone schedules", _siegeZoneSchedules.Count);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT zone_group_id, week_start FROM siege_plans ORDER BY week_start";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var zoneGroupId = reader.GetUInt32("zone_group_id");
                var weekStart = reader.GetDateTime("week_start");
                if (!_siegePlanWeekStartsByZoneGroup.TryGetValue(zoneGroupId, out var list))
                    _siegePlanWeekStartsByZoneGroup[zoneGroupId] = list = [];
                list.Add(weekStart);
            }
        }

        var planCount = _siegePlanWeekStartsByZoneGroup.Values.Sum(l => l.Count);
        Logger.Info("Loaded {0} siege plan cycles across {1} zone groups", planCount, _siegePlanWeekStartsByZoneGroup.Count);

        LoadLodestoneTemplates(connection);
    }

    /// <summary>
    /// Claimable Guard Towers: <c>housings.guard_tower_setting_id</c> and any housing whose build-step skill
    /// applies <see cref="SpecialType.DeclareDominion"/>. Zone group is the live house's cell, not a
    /// compiled map.
    /// </summary>
    private void LoadLodestoneTemplates(SqliteConnection connection)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id, guard_tower_setting_id FROM housings";
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
            {
                var housingId = reader.GetUInt32("id");
                var settingId = reader.GetUInt32("guard_tower_setting_id", 0);
                if (DominionClaimRules.IsLodestoneTemplate(settingId, false))
                    _lodestoneTemplateIds.Add(housingId);
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT DISTINCT hbs.housing_id
                FROM housing_build_steps hbs
                JOIN skill_effects se ON se.skill_id = hbs.skill_id
                JOIN effects e ON e.id = se.effect_id AND e.actual_type = 'SpecialEffect'
                JOIN special_effects sx ON sx.id = e.actual_id
                WHERE sx.special_effect_type_id = @declareType
                """;
            command.Parameters.AddWithValue("@declareType", (int)SpecialType.DeclareDominion);
            command.Prepare();
            using var sqliteReader = command.ExecuteReader();
            using var reader = new SQLiteWrapperReader(sqliteReader);
            while (reader.Read())
                _lodestoneTemplateIds.Add(reader.GetUInt32("housing_id"));
        }

        Logger.Info("Loaded {0} lodestone housing templates", _lodestoneTemplateIds.Count);
    }

    public void PostLoad()
    {
    }

    public GuardTowerSettings GetGuardTowerSettings(uint id) => _guardTowerSettings.GetValueOrDefault(id);

    /// <summary>Ordered step list (1, 2, 3, ...) for a guard_tower_setting_id, or empty if none defined.</summary>
    public IReadOnlyList<GuardTowerStep> GetGuardTowerSteps(uint guardTowerSettingId) =>
        (IReadOnlyList<GuardTowerStep>)_guardTowerStepsBySettingId.GetValueOrDefault(guardTowerSettingId) ?? [];

    /// <summary>Highest defined step number for a guard_tower_setting_id, or 0 if none.</summary>
    public int GetMaxGuardTowerStep(uint guardTowerSettingId) =>
        GetGuardTowerSteps(guardTowerSettingId).Count == 0 ? 0 : GetGuardTowerSteps(guardTowerSettingId)[^1].Step;

    /// <summary>True if this housing design (== item_housings.design_id == housings.template_id) is one of the
    /// 5 unique per-territory dominion buildings from dominion_housings, i.e. only one should ever be built per
    /// claimed zone group. False for anything else (Wall/Gate/Tower designs included) - those may be built
    /// repeatedly.</summary>
    public bool IsUniqueDominionHousingDesign(uint designId) => _uniqueDominionHousingDesigns.Contains(designId);

    public SiegeZoneSchedule GetSiegeZoneSchedule(uint zoneGroupId) => _siegeZoneSchedules.GetValueOrDefault(zoneGroupId);

    public IEnumerable<uint> ScheduledZoneGroupIds => _siegeZoneSchedules.Keys;

    public bool IsLodestoneTemplate(uint housingTemplateId) =>
        housingTemplateId != 0 && _lodestoneTemplateIds.Contains(housingTemplateId);

    /// <summary>The latest siege_plans week_start for <paramref name="zoneGroupId"/> that is not after <paramref name="atUtc"/>, or null if none.</summary>
    public DateTime? GetCurrentCycleWeekStart(uint zoneGroupId, DateTime atUtc)
    {
        if (!_siegePlanWeekStartsByZoneGroup.TryGetValue(zoneGroupId, out var weekStarts))
            return null;
        return SiegeScheduleRules.CurrentCycleWeekStart(weekStarts, atUtc);
    }
}
