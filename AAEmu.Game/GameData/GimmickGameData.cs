using AAEmu.Commons.Utils;
using AAEmu.Game.GameData.Framework;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Utils.DB;

using Microsoft.Data.Sqlite;
using NLog;

namespace AAEmu.Game.GameData;

[GameData]
public class GimmickGameData : Singleton<GimmickGameData>, IGameDataLoader
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();

    private Dictionary<uint, GimmickTemplate> _templates;

    public bool Exist(uint templateId)
    {
        return _templates.ContainsKey(templateId);
    }

    public GimmickTemplate GetGimmickTemplate(uint id)
    {
        return _templates.GetValueOrDefault(id);
    }
    
    public void Load(SqliteConnection connection)
    {
        _templates = [];

        Logger.Info("Loading gimmick templates...");

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT * FROM gimmicks";
        command.Prepare();
        using var reader = new SQLiteWrapperReader(command.ExecuteReader());
        while (reader.Read())
        {
            var template = new GimmickTemplate
            {
                Id = reader.GetUInt32("id"), // GimmickId
                AirResistance = reader.GetFloat("air_resistance"),
                CollisionMinSpeed = reader.GetFloat("collision_min_speed"),
                //template.CollisionSkillId = reader.GetUInt32("collision_skill_id");
                //template.CollisionSkillId = reader.IsDBNull("collision_skill_id") ? 0 : reader.GetUInt32("collision_skill_id");
                CollisionSkillId = reader.GetUInt32("collision_skill_id", 0),
                CollisionUnitOnly = reader.GetBoolean("collision_unit_only"),
                Damping = reader.GetFloat("damping"),
                Density = reader.GetFloat("density"),
                DisappearByCollision = reader.GetBoolean("disappear_by_collision"),
                FadeInDuration = reader.GetUInt32("fade_in_duration"),
                FadeOutDuration = reader.GetUInt32("fade_out_duration"),
                FreeFallDamping = reader.GetFloat("free_fall_damping"),
                Graspable = reader.GetBoolean("graspable"),
                Gravity = reader.GetFloat("gravity"),
                LifeTime = reader.GetUInt32("life_time"),
                Mass = reader.GetFloat("mass"),
                ModelPath = reader.GetString("model_path"),
                Name = reader.GetString("name"),
                NoGroundCollider = reader.GetBoolean("no_ground_collider"),
                PushableByPlayer = reader.GetBoolean("pushable_by_player"),
                SkillDelay = reader.GetUInt32("skill_delay"),
                //template.SkillId = reader.GetUInt32("skill_id");
                //template.CollisionSkillId = reader.IsDBNull("skill_id") ? 0 : reader.GetUInt32("skill_id");
                SkillId = reader.GetUInt32("skill_id", 0),
                SpawnDelay = reader.GetUInt32("spawn_delay"),
                WaterDamping = reader.GetFloat("water_damping"),
                WaterDensity = reader.GetFloat("water_density"),
                WaterResistance = reader.GetFloat("water_resistance")
            };

            _templates.Add(template.Id, template);
        }
    }

    public void PostLoad()
    {
        //
    }
}
