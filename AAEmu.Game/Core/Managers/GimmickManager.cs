using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models.Game.Faction;
using AAEmu.Game.Models.Game.Gimmicks;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.Tasks;
using AAEmu.Game.Utils.DB;

using NLog;

using static System.String;

namespace AAEmu.Game.Core.Managers;

public class GimmickManager : Singleton<GimmickManager>
{
    private static Logger Logger { get; } = LogManager.GetCurrentClassLogger();
    private bool _loaded = false;

    private Dictionary<uint, GimmickTemplate> _templates;
    private Dictionary<uint, Gimmick> _activeGimmicks;
    private const double Delay = 50;
    //private const double DelayInit = 1;
    private Task GimmickTickTask { get; set; }

    public bool Exist(uint templateId)
    {
        return _templates.ContainsKey(templateId);
    }

    public GimmickTemplate GetGimmickTemplate(uint id)
    {
        return _templates.GetValueOrDefault(id);
    }

    /// <summary>
    /// Create for spawning elevators
    /// </summary>
    /// <param name="objectId"></param>
    /// <param name="templateId"></param>
    /// <param name="spawner"></param>
    /// <returns></returns>
    public Gimmick Create(uint objectId, uint templateId, GimmickSpawner spawner)
    {
        /*
         * for elevators: templateId=0 and Template=null, but EntityGuid is used
         */

        var gimmick = new Gimmick();
        if (templateId != 0)
        {
            var template = _templates[templateId];
            //var template = GetGimmickTemplate(templateId);
            if (template == null) { return null; }
            gimmick.Template = template;
            gimmick.ModelPath = template.ModelPath;
            gimmick.EntityGuid = 0;
        }
        else
        {
            gimmick.Template = null;
            gimmick.ModelPath = Empty;
            gimmick.EntityGuid = spawner.EntityGuid;
        }

        gimmick.ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId();
        gimmick.Spawner = spawner;
        gimmick.TemplateId = templateId;
        gimmick.Faction = new SystemFaction();
        gimmick.Transform.ApplyWorldSpawnPosition(spawner.Position);
        gimmick.Vel = new Vector3(0f, 0f, 0f);
        gimmick.Rot = new Quaternion(spawner.RotationX, spawner.RotationY, spawner.RotationZ, spawner.RotationW);
        gimmick.ModelParams = new UnitCustomModelParams();
        gimmick.SetScale(spawner.Scale);

        if (gimmick.Transform.World.IsOrigin())
        {
            Logger.Error($"Can't spawn gimmick {templateId}");
            return null;
        }

        gimmick.Spawn(); // adding to the world
        _activeGimmicks.Add(gimmick.ObjId, gimmick);

        return gimmick;
    }

    /// <summary>
    /// Create for spawning projectiles
    /// </summary>
    /// <param name="templateId"></param>
    /// <returns></returns>
    public Gimmick Create(uint templateId)
    {
        var template = _templates[templateId];
        if (template == null) { return null; }

        var gimmick = new Gimmick();
        gimmick.ObjId = ObjectIdManager.Instance.GetNextId();
        gimmick.Spawner = new GimmickSpawner();
        gimmick.Template = template;
        gimmick.TemplateId = template.Id;
        gimmick.Faction = new SystemFaction();
        gimmick.ModelPath = template.ModelPath;
        gimmick.ModelParams = new UnitCustomModelParams();

        return gimmick;
    }

    public void Load()
    {
        if (_loaded)
            return;

        _templates = new Dictionary<uint, GimmickTemplate>();
        _activeGimmicks = new Dictionary<uint, Gimmick>();

        Logger.Info("Loading gimmick templates...");

        #region SQLLite
        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM gimmicks";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var template = new GimmickTemplate();

                        template.Id = reader.GetUInt32("id"); // GimmickId
                        template.AirResistance = reader.GetFloat("air_resistance");
                        template.CollisionMinSpeed = reader.GetFloat("collision_min_speed");
                        //template.CollisionSkillId = reader.GetUInt32("collision_skill_id");
                        //template.CollisionSkillId = reader.IsDBNull("collision_skill_id") ? 0 : reader.GetUInt32("collision_skill_id");
                        template.CollisionSkillId = reader.GetUInt32("collision_skill_id", 0);

                        template.CollisionUnitOnly = reader.GetBoolean("collision_unit_only");
                        template.Damping = reader.GetFloat("damping");
                        template.Density = reader.GetFloat("density");
                        template.DisappearByCollision = reader.GetBoolean("disappear_by_collision");
                        template.FadeInDuration = reader.GetUInt32("fade_in_duration");
                        template.FadeOutDuration = reader.GetUInt32("fade_out_duration");
                        template.FreeFallDamping = reader.GetFloat("free_fall_damping");
                        template.Graspable = reader.GetBoolean("graspable");
                        template.Gravity = reader.GetFloat("gravity");
                        template.LifeTime = reader.GetUInt32("life_time");
                        template.Mass = reader.GetFloat("mass");
                        template.ModelPath = reader.GetString("model_path");
                        template.Name = reader.GetString("name");
                        template.NoGroundCollider = reader.GetBoolean("no_ground_collider");
                        template.PushableByPlayer = reader.GetBoolean("pushable_by_player");
                        template.SkillDelay = reader.GetUInt32("skill_delay");
                        //template.SkillId = reader.GetUInt32("skill_id");
                        //template.CollisionSkillId = reader.IsDBNull("skill_id") ? 0 : reader.GetUInt32("skill_id");
                        template.SkillId = reader.GetUInt32("skill_id", 0);

                        template.SpawnDelay = reader.GetUInt32("spawn_delay");
                        template.WaterDamping = reader.GetFloat("water_damping");
                        template.WaterDensity = reader.GetFloat("water_density");
                        template.WaterResistance = reader.GetFloat("water_resistance");

                        _templates.Add(template.Id, template);
                    }
                }
            }
        }
        #endregion

        _loaded = true;
    }

    public void Initialize()
    {
        Logger.Warn("GimmickTickTask: Started");

        //GimmickTickTask = new GimmickTickStartTask();
        //TaskManager.Instance.Schedule(GimmickTickTask, TimeSpan.FromMinutes(DelayInit));
        TickManager.Instance.OnTick.Subscribe(GimmickTick, TimeSpan.FromMilliseconds(Delay), true);
    }

    private void GimmickTick(TimeSpan delta)
    {
        var activeGimmicks = GetActiveGimmicks();
        foreach (var gimmick in activeGimmicks)
        {
            GimmickTick(gimmick);
        }

        //TaskManager.Instance.Schedule(GimmickTickTask, TimeSpan.FromMilliseconds(Delay));
    }
    internal void GimmickTick()
    {
        var activeGimmicks = GetActiveGimmicks();
        foreach (var gimmick in activeGimmicks)
        {
            GimmickTick(gimmick);
        }

        TaskManager.Instance.Schedule(GimmickTickTask, TimeSpan.FromMilliseconds(Delay));
    }

    private Gimmick[] GetActiveGimmicks()
    {
        lock (_activeGimmicks)
        {
            return _activeGimmicks.Values.ToArray();
        }
    }

    private static void GimmickTick(Gimmick gimmick)
    {
        if (gimmick.TimeLeft > 0)
            return;

        const float maxVelocity = 4.5f;
        const float deltaTime = 0.05f;

        var position = gimmick.Transform.World.Position;
        var velocityZ = gimmick.Vel.Z;

        var middleTarget = position with { Z = gimmick.Spawner.MiddleZ };
        var topTarget = position with { Z = gimmick.Spawner.TopZ };
        var bottomTarget = position with { Z = gimmick.Spawner.BottomZ };

        var isMovingDown = gimmick.moveDown;
        var isInMiddleZ = gimmick.Spawner.MiddleZ > 0;

        if (isInMiddleZ)
        {
            if (position.Z < gimmick.Spawner.MiddleZ && gimmick.Vel.Z >= 0 && !isMovingDown)
                MoveAlongZAxis(ref gimmick, ref position, middleTarget, maxVelocity, deltaTime, ref velocityZ);
            else if (position.Z < gimmick.Spawner.TopZ && gimmick.Vel.Z >= 0 && !isMovingDown)
                MoveAlongZAxis(ref gimmick, ref position, topTarget, maxVelocity, deltaTime, ref velocityZ);
            else if (position.Z > gimmick.Spawner.MiddleZ && gimmick.Vel.Z <= 0 && isMovingDown)
                MoveAlongZAxis(ref gimmick, ref position, middleTarget, maxVelocity, deltaTime, ref velocityZ);
            else if (position.Z > gimmick.Spawner.BottomZ && gimmick.Vel.Z <= 0 && isMovingDown)
                MoveAlongZAxis(ref gimmick, ref position, bottomTarget, maxVelocity, deltaTime, ref velocityZ);
        }
        else
        {
            if (position.Z < gimmick.Spawner.TopZ && gimmick.Vel.Z >= 0)
                MoveAlongZAxis(ref gimmick, ref position, topTarget, maxVelocity, deltaTime, ref velocityZ);
            else
                MoveAlongZAxis(ref gimmick, ref position, bottomTarget, maxVelocity, deltaTime, ref velocityZ);
        }

        gimmick.Transform.Local.SetHeight(position.Z);

        var isMoving = Math.Abs(gimmick.Vel.Z) > 0;
        gimmick.Time += 50;
        gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);

        if (isMoving)
            return;

        gimmick.WaitTime = DateTime.UtcNow.AddSeconds(gimmick.Spawner.WaitTime);

        if (position.Z > gimmick.Spawner.BottomZ && isMovingDown)
        {
            gimmick.moveDown = true;
        }
        else if (position.Z < gimmick.Spawner.TopZ && !isMovingDown)
        {
            gimmick.moveDown = false;
        }
        else
            gimmick.moveDown = !gimmick.moveDown;
    }

    private static void MoveAlongZAxis(ref Gimmick gimmick, ref Vector3 position, Vector3 target, float maxVelocity, float deltaTime, ref float velocityZ)
    {
        var distance = target - position;
        velocityZ = maxVelocity * Math.Sign(distance.Z);
        var movingDistance = velocityZ * deltaTime;

        if (Math.Abs(distance.Z) >= Math.Abs(movingDistance))
        {
            position.Z += movingDistance;
            gimmick.Vel = gimmick.Vel with { Z = velocityZ };
        }
        else
        {
            position.Z = target.Z;
            gimmick.Vel = Vector3.Zero;
        }
    }
}

