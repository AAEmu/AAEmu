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

namespace AAEmu.Game.Core.Managers
{
    public class GimmickManager : Singleton<GimmickManager>
    {
        private static Logger _log = LogManager.GetCurrentClassLogger();
        private bool _loaded = false;
        
        private Dictionary<uint, GimmickTemplate> _templates;
        private Dictionary<uint, Gimmick> _activeGimmicks;
        private const double Delay = 50;
        private const double DelayInit = 1;
        private Task GimmickTickTask { get; set; }

        public bool Exist(uint templateId)
        {
            return _templates.ContainsKey(templateId);
        }

        public GimmickTemplate GetGimmickTemplate(uint id)
        {
            return _templates.ContainsKey(id) ? _templates[id] : null;
        }

        public Gimmick Create(uint objectId, uint templateId, GimmickSpawner spawner)
        {
            if (!_templates.ContainsKey(templateId))
            {
                return null;
            }

            var template = _templates[templateId];
            //var template = GetGimmickTemplate(templateId);

            var gimmick = new Gimmick();
            gimmick.ObjId = objectId > 0 ? objectId : ObjectIdManager.Instance.GetNextId();
            gimmick.Spawner = spawner;
            gimmick.Template = template;
            gimmick.GimmickId = gimmick.ObjId;
            gimmick.TemplateId = template.Id; // duplicate Id
            gimmick.Id = template.Id;
            gimmick.Faction = new SystemFaction();
            gimmick.ModelPath = template.ModelPath;
            gimmick.Patrol = null;
            gimmick.Transform.ApplyWorldSpawnPosition(spawner.Position);
            gimmick.Vel = new Vector3(0f, 0f, 0f);
            gimmick.Rot = new Quaternion(spawner.RotationX, spawner.RotationY, spawner.RotationZ, spawner.RotationW);
            gimmick.ModelParams = new UnitCustomModelParams();

            gimmick.Spawn();
            _activeGimmicks.Add(gimmick.ObjId, gimmick);

            return gimmick;
        }

        public void Load()
        {
            if (_loaded)
                return;
            
            _templates = new Dictionary<uint, GimmickTemplate>();
            _activeGimmicks = new Dictionary<uint, Gimmick>();

            _log.Info("Loading gimmick templates...");

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
            _log.Warn("GimmickTickTask: Started");

            //GimmickTickTask = new GimmickTickStartTask();
            //TaskManager.Instance.Schedule(GimmickTickTask, TimeSpan.FromMinutes(DelayInit));
            TickManager.Instance.OnTick.Subscribe(GimmickTick, TimeSpan.FromMilliseconds(Delay), true);
        }
        internal void GimmickTick(TimeSpan delta)
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
            return _activeGimmicks.Values.ToArray();
        }

        private static void GimmickTick(Gimmick gimmick)
        {
            if (gimmick.TimeLeft > 0)
            {
                return;
            }
            var maxVelocityForward = 4.5f;
            var maxVelocityBackward = -4.5f;
            var deltaTime = 0.05f;
            var movingDistance = 0.27f;

            Vector3 vPosition;
            Vector3 vTarget;
            Vector3 vDistance;

            var velocityZ = gimmick.Vel.Z;
            var positionZ = gimmick.Transform.World.Position.Z;
            vPosition = gimmick.Transform.World.ClonePosition();

            if (gimmick.Spawner.MiddleZ > 0)
            {
                if (positionZ < gimmick.Spawner.MiddleZ && gimmick.Vel.Z >= 0 && !gimmick.moveDown)
                {
                    vTarget = new Vector3(gimmick.Transform.World.Position.X, gimmick.Transform.World.Position.Y, gimmick.Spawner.MiddleZ);
                    vDistance = vTarget - vPosition; // dx, dy, dz
                    velocityZ = maxVelocityForward;

                    movingDistance = velocityZ * deltaTime;

                    if (Math.Abs(vDistance.Z) >= Math.Abs(movingDistance))
                    {
                        positionZ += movingDistance;
                        gimmick.Vel = new Vector3(gimmick.Vel.X, gimmick.Vel.Y, velocityZ);
                    }
                    else
                    {
                        positionZ = vTarget.Z;
                        gimmick.Vel = new Vector3(0f, 0f, 0f);
                    }
                }
                else if (vPosition.Z < gimmick.Spawner.TopZ && gimmick.Vel.Z >= 0 && !gimmick.moveDown)
                {
                    vTarget = new Vector3(gimmick.Transform.World.Position.X, gimmick.Transform.World.Position.Y, gimmick.Spawner.TopZ);
                    vDistance = vTarget - vPosition; // dx, dy, dz
                    velocityZ = maxVelocityForward;
                    movingDistance = velocityZ * deltaTime;

                    if (Math.Abs(vDistance.Z) >= Math.Abs(movingDistance))
                    {
                        positionZ += movingDistance;
                        gimmick.Vel = new Vector3(gimmick.Vel.X, gimmick.Vel.Y, velocityZ);
                        gimmick.moveDown = false;
                    }
                    else
                    {
                        positionZ = vTarget.Z;
                        gimmick.Vel = new Vector3(0f, 0f, 0f);
                        gimmick.moveDown = true;
                    }
                }
                else if (vPosition.Z > gimmick.Spawner.MiddleZ && gimmick.Vel.Z <= 0 && gimmick.moveDown)
                {
                    vTarget = new Vector3(gimmick.Transform.World.Position.X, gimmick.Transform.World.Position.Y, gimmick.Spawner.MiddleZ);
                    vDistance = vTarget - vPosition; // dx, dy, dz
                    velocityZ = maxVelocityBackward;
                    movingDistance = velocityZ * deltaTime;

                    if (Math.Abs(vDistance.Z) >= Math.Abs(movingDistance))
                    {
                        positionZ += movingDistance;
                        gimmick.Vel = new Vector3(gimmick.Vel.X, gimmick.Vel.Y, velocityZ);
                    }
                    else
                    {
                        positionZ = vTarget.Z;
                        gimmick.Vel = new Vector3(0f, 0f, 0f);
                    }
                }
                else
                {
                    vTarget = new Vector3(gimmick.Transform.World.Position.X, gimmick.Transform.World.Position.Y, gimmick.Spawner.BottomZ);
                    vDistance = vTarget - vPosition; // dx, dy, dz
                    velocityZ = maxVelocityBackward;
                    movingDistance = velocityZ * deltaTime;

                    if (Math.Abs(vDistance.Z) >= Math.Abs(movingDistance))
                    {
                        positionZ += movingDistance;
                        gimmick.Vel = new Vector3(gimmick.Vel.X, gimmick.Vel.Y, velocityZ);
                        gimmick.moveDown = true;
                    }
                    else
                    {
                        positionZ = vTarget.Z;
                        gimmick.Vel = new Vector3(0f, 0f, 0f);
                        gimmick.moveDown = false;
                    }
                }
            }
            else
            {
                if (vPosition.Z < gimmick.Spawner.TopZ && gimmick.Vel.Z >= 0)
                {
                    vTarget = new Vector3(gimmick.Transform.World.Position.X, gimmick.Transform.World.Position.Y, gimmick.Spawner.TopZ);
                    vDistance = vTarget - vPosition; // dx, dy, dz
                    velocityZ = maxVelocityForward;

                    movingDistance = velocityZ * deltaTime;

                    if (Math.Abs(vDistance.Z) >= Math.Abs(movingDistance))
                    {
                        positionZ += movingDistance;
                        gimmick.Vel = new Vector3(gimmick.Vel.X, gimmick.Vel.Y, velocityZ);
                    }
                    else
                    {
                        positionZ = vTarget.Z;
                        gimmick.Vel = new Vector3(0f, 0f, 0f);
                    }
                }
                else
                {
                    vTarget = new Vector3(gimmick.Transform.World.Position.X, gimmick.Transform.World.Position.Y, gimmick.Spawner.BottomZ);
                    vDistance = vTarget - vPosition; // dx, dy, dz
                    velocityZ = maxVelocityBackward;
                    movingDistance = velocityZ * deltaTime;

                    if (Math.Abs(vDistance.Z) >= Math.Abs(movingDistance))
                    {
                        positionZ += movingDistance;
                        gimmick.Vel = new Vector3(gimmick.Vel.X, gimmick.Vel.Y, velocityZ);
                    }
                    else
                    {
                        positionZ = vTarget.Z;
                        gimmick.Vel = new Vector3(0f, 0f, 0f);
                    }
                }
            }

            // TODO: replace this with .World.SetHeight after .World is correctly implemented
            gimmick.Transform.Local.SetHeight(positionZ);

            // If the number of executions is less than the angle, continue adding tasks or stop moving
            if (Math.Abs(gimmick.Vel.Z) > 0)
            {
                gimmick.Time += 50;    // has to change all the time for normal motion.
                gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);
            }
            else
            {
                // stop for a few seconds
                gimmick.Time += 50;    // has to change all the time for normal motion.
                gimmick.BroadcastPacket(new SCGimmickMovementPacket(gimmick), true);
                gimmick.WaitTime = DateTime.UtcNow.AddSeconds(gimmick.Spawner.WaitTime);
            }
        }
    }
}

