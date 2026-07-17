using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers;

/// <summary>
/// Менеджер моделей, загружающий данные из таблиц <c>actor_models</c>, <c>ship_models</c>,
/// <c>vehicle_models</c>, <c>models</c> и <c>game_stances</c> БД <c>compact.sqlite3</c>.
/// </summary>
public class ModelManager : Singleton<ModelManager>, IModelManager
{

    private Dictionary<string, Dictionary<uint, Model>> _models;
    private Dictionary<uint, ModelType> _modelTypes;
    private Dictionary<uint, GameStance> _gameStances;
    private bool _loaded = false;

    // Getters
    public ModelType GetModelType(uint modelId)
    {
        if (_modelTypes.TryGetValue(modelId, out var res))
            return res;
        return null;
    }

    public ActorModel GetActorModel(uint modelId)
    {
        if (!_modelTypes.TryGetValue(modelId, out var modelType))
            return null;
        if (!_models.TryGetValue(modelType.SubType, out var value) || !value.TryGetValue(modelType.SubId, out var model))
            return null;
        if (model is ActorModel actorModel)
            return actorModel;
        return null;
    }

    public ShipModelV1 GetShipModel(uint modelId)
    {
        if (!_modelTypes.TryGetValue(modelId, out var modelType))
            return null;
        if (!_models.TryGetValue(modelType.SubType, out var value) || !value.TryGetValue(modelType.SubId, out var model))
            return null;
        if (model is ShipModelV1 shipModel)
            return shipModel;
        return null;
    }

    public VehicleModel GetVehicleModels(uint modelId)
    {
        if (!_modelTypes.TryGetValue(modelId, out var modelType))
            return null;
        if (!_models.TryGetValue(modelType.SubType, out var value) || !value.TryGetValue(modelType.SubId, out var model))
            return null;
        if (model is VehicleModel vehicleModel)
            return vehicleModel;
        return null;
    }

    public bool IsFlyOrSwim(uint modelId)
    {
        if (!_modelTypes.TryGetValue(modelId, out var modelType))
            return false;
        if (!_models.TryGetValue(modelType.SubType, out var value) || !value.TryGetValue(modelType.SubId, out var model))
            return false;
        return model is ActorModel { MovementId: 2 };
    }

    /// <summary>
    /// Загружает модели из таблиц <c>actor_models</c>, <c>ship_models</c>, <c>vehicle_models</c>,
    /// <c>models</c> и <c>game_stances</c>.
    /// </summary>
    /// <remarks>
    /// Схемы таблиц (проверены по compact.sqlite3):
    /// <list type="bullet">
    ///   <item><description><c>actor_models</c>: id (PK) + ~70 полей физики/графики актёра</description></item>
    ///   <item><description><c>ship_models</c>: id (PK) + ~50 полей физики корабля</description></item>
    ///   <item><description><c>vehicle_models</c>: id (PK) + ~60 полей физики транспорта</description></item>
    ///   <item><description><c>models</c>: id (PK), sub_id, sub_type и флаги отображения</description></item>
    ///   <item><description><c>game_stances</c>: id (PK), actor_model_id, stance_id и параметры позы</description></item>
    /// </list>
    /// </remarks>
    public void Load()
    {
        if (_loaded)
            return;

        _models = new Dictionary<string, Dictionary<uint, Model>>
                {
                    {"ActorModel", new Dictionary<uint, Model>()},
                    {"VehicleModel", new Dictionary<uint, Model>()},
                    {"PrefabModel", new Dictionary<uint, Model>()},
                    {"ShipModel", new Dictionary<uint, Model>()}
                };

        _modelTypes = [];
        _gameStances = [];

        using (var connection = SQLite.CreateConnection())
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM actor_models";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var model = new ActorModel
                        {
                            Id = reader.GetUInt32("id"),
                            Radius = reader.GetFloat("radius"),
                            Height = reader.GetFloat("height"),
                            MovementId = reader.GetInt32("movement_id"),
                            ActorHeight = reader.GetFloat("actor_height"),
                            AddBox = reader.GetBoolean("add_box"),
                            AnimationGraph = reader.GetString("animation_graph"),
                            AttackStartRange = reader.GetFloat("attack_start_range"),
                            BeanstalkBack = reader.GetFloat("beanstalk_back"),
                            BoxX = reader.GetFloat("box_x"),
                            BoxY = reader.GetFloat("box_y"),
                            BoxZ = reader.GetFloat("box_z"),
                            CenterX = reader.GetFloat("center_x"),
                            CenterY = reader.GetFloat("center_y"),
                            CenterZ = reader.GetFloat("center_z"),
                            FaceTargetInstantly = reader.GetBoolean("face_target_instantly"),
                            FlyMode = reader.GetBoolean("fly_mode"),
                            FxScale = reader.GetFloat("fx_scale"),
                            GameBackwardDiagonalMultiplier = reader.GetFloat("game_backward_diagonal_multiplier"),
                            GameBackwardMultiplier = reader.GetFloat("game_backward_multiplier"),
                            GameBowLookIkBlendHead = reader.GetFloat("game_bow_look_ik_blend_head"),
                            GameBowLookIkBlendNeck = reader.GetFloat("game_bow_look_ik_blend_neck"),
                            GameBowLookIkBlendSpine1 = reader.GetFloat("game_bow_look_ik_blend_spine1"),
                            GameBowLookIkBlendSpine2 = reader.GetFloat("game_bow_look_ik_blend_spine2"),
                            GameBowLookIkBlendSpine3 = reader.GetFloat("game_bow_look_ik_blend_spine3"),
                            GameForwardDiagonalMultiplier = reader.GetFloat("game_forward_diagonal_multiplier"),
                            GameForwardMultiplier = reader.GetFloat("game_forward_multiplier"),
                            GameGrabMultiplier = reader.GetFloat("game_grab_multiplier"),
                            GameInertia = reader.GetFloat("game_inertia"),
                            GameInertiaAccel = reader.GetFloat("game_inertia_accel"),
                            GameJumpHeight = reader.GetFloat("game_jump_height"),
                            GameLeanAngle = reader.GetInt32("game_lean_angle"),
                            GameLeanShift = reader.GetFloat("game_lean_shift"),
                            GameLookIkBlendHead = reader.GetFloat("game_look_ik_blend_head"),
                            GameLookIkBlendNeck = reader.GetFloat("game_look_ik_blend_neck"),
                            GameLookIkBlendSpine1 = reader.GetFloat("game_look_ik_blend_spine1"),
                            GameLookIkBlendSpine2 = reader.GetFloat("game_look_ik_blend_spine2"),
                            GameLookIkBlendSpine3 = reader.GetFloat("game_look_ik_blend_spine3"),
                            GameMaxGrabMass = reader.GetInt32("game_max_grab_mass"),
                            GameMaxGrabVolume = reader.GetFloat("game_max_grab_volume"),
                            GameSprintMultiplier = reader.GetFloat("game_sprint_multiplier"),
                            GameStrafeMultiplier = reader.GetFloat("game_strafe_multiplier"),
                            GameWalkBackwardDiagonalMultiplier = reader.GetFloat("game_walk_backward_diagonal_multiplier"),
                            GameWalkBackwardMultiplier = reader.GetFloat("game_walk_backward_multiplier"),
                            GameWalkForwardDiagonalMultiplier = reader.GetFloat("game_walk_forward_diagonal_multiplier"),
                            GameWalkMultiplier = reader.GetFloat("game_walk_multiplier"),
                            GameWalkStrafeMultiplier = reader.GetFloat("game_walk_strafe_multiplier"),
                            GroundTargetable = reader.GetBoolean("ground_targetable"),
                            HandRate = reader.GetFloat("hand_rate"),
                            HitPower = reader.GetInt32("hit_power"),
                            HropeDown = reader.GetFloat("hrope_down"),
                            ModelFile = reader.GetString("model_file"),
                            PhysicsFlags = reader.GetInt32("physics_flags"),
                            PhysicsLivingAirResistance = reader.GetFloat("physics_living_air_resistance"),
                            PhysicsLivingColliderMat = reader.GetString("physics_living_collider_mat"),
                            PhysicsLivingGravity = reader.GetFloat("physics_living_gravity"),
                            PhysicsLivingKAirControl = reader.GetFloat("physics_living_k_air_control"),
                            PhysicsLivingMass = reader.GetInt32("physics_living_mass"),
                            PhysicsLivingMaxClimbAngle = reader.GetFloat("physics_living_max_climb_angle"),
                            PhysicsLivingMaxVelGround = reader.GetInt32("physics_living_max_vel_ground"),
                            PhysicsLivingMinFallAngle = reader.GetFloat("physics_living_min_fall_angle"),
                            PhysicsLivingMinSlideAngle = reader.GetFloat("physics_living_min_slide_angle"),
                            PhysicsLivingTimeImpulseRecover = reader.GetFloat("physics_living_time_impulse_recover"),
                            PhysicsMass = reader.GetInt32("physics_mass"),
                            PhysicsStiffnessScale = reader.GetInt32("physics_stiffness_scale"),
                            Portrait = reader.GetString("portrait"),
                            PushRagdoll = reader.GetBoolean("push_ragdoll"),
                            RestrictBoardingMate = reader.GetBoolean("restrict_boarding_mate"),
                            RestrictBoardingSlave = reader.GetBoolean("restrict_boarding_slave"),
                            RestrictClimb = reader.GetBoolean("restrict_climb"),
                            RopeBack = reader.GetFloat("rope_back"),
                            RopeHangingHandOffsetX = reader.GetFloat("rope_hanging_hand_offset_x"),
                            RopeHangingHandOffsetY = reader.GetFloat("rope_hanging_hand_offset_y"),
                            RopeHangingHandOffsetZ = reader.GetFloat("rope_hanging_hand_offset_z"),
                            SharedDummyModel = reader.GetBoolean("shared_dummy_model"),
                            SightFov = reader.GetFloat("sight_fov"),
                            SightRange = reader.GetFloat("sight_range"),
                            SlopeAlignment = reader.GetBoolean("slope_alignment"),
                            TurnSpeed = reader.GetFloat("turn_speed"),
                            UnderwaterCreature = reader.GetBoolean("underwater_creature"),
                            UpperbodyGraph = reader.GetString("upperbody_graph"),
                            UseRagdoll = reader.GetBoolean("use_ragdoll"),
                            UseRagdollHit = reader.GetBoolean("use_ragdoll_hit"),
                            UseRagdollKnockDown = reader.GetBoolean("use_ragdoll_knock_down"),
                            UseRandomIdleControl = reader.GetBoolean("use_random_idle_control")
                        };

                        _models["ActorModel"].TryAdd(model.Id, model);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM ship_models";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var model = new ShipModelV1
                        {
                            Id = reader.GetUInt32("id"),
                            Velocity = reader.GetFloat("velocity"),
                            Mass = reader.GetFloat("mass"),
                            MassCenterX = reader.GetFloat("mass_center_x"),
                            MassCenterY = reader.GetFloat("mass_center_y"),
                            MassCenterZ = reader.GetFloat("mass_center_z"),
                            MassBoxSizeX = reader.GetFloat("mass_box_size_x"),
                            MassBoxSizeY = reader.GetFloat("mass_box_size_y"),
                            MassBoxSizeZ = reader.GetFloat("mass_box_size_z"),
                            WaterDensity = reader.GetFloat("water_density", 1f),
                            WaterResistance = reader.GetFloat("water_resistance", 1f),
                            SteerVel = reader.GetFloat("steer_vel"),
                            Accel = reader.GetFloat("accel"),
                            ReverseAccel = reader.GetFloat("reverse_accel"),
                            ReverseVelocity = reader.GetFloat("reverse_velocity"),
                            TurnAccel = reader.GetFloat("turn_accel"),
                            TubeLength = reader.GetFloat("tube_length"),
                            TubeRadius = reader.GetFloat("tube_radius"),
                            TubeOffsetZ = reader.GetFloat("tube_offset_z"),
                            KeelLength = reader.GetFloat("keel_length"),
                            KeelHeight = reader.GetFloat("keel_height"),
                            KeelOffsetZ = reader.GetFloat("keel_offset_z"),
                            AccelExponent = reader.GetFloat("accel_exponent"),
                            CharAnimSteerBackwardId = reader.GetInt32("char_anim_steer_backward_id"),
                            CharAnimSteerForwardId = reader.GetInt32("char_anim_steer_forward_id"),
                            CollisionBoxCenterX = reader.GetFloat("collision_box_center_x"),
                            CollisionBoxCenterY = reader.GetFloat("collision_box_center_y"),
                            CollisionBoxCenterZ = reader.GetFloat("collision_box_center_z"),
                            CollisionBoxOffsetX = reader.GetFloat("collision_box_offset_x"),
                            CollisionBoxOffsetY = reader.GetFloat("collision_box_offset_y"),
                            CollisionBoxOffsetZ = reader.GetFloat("collision_box_offset_z"),
                            CollisionBoxScaleX = reader.GetFloat("collision_box_scale_x"),
                            CollisionBoxScaleY = reader.GetFloat("collision_box_scale_y"),
                            CollisionBoxScaleZ = reader.GetFloat("collision_box_scale_z"),
                            CollisionBoxSizeX = reader.GetFloat("collision_box_size_x"),
                            CollisionBoxSizeY = reader.GetFloat("collision_box_size_y"),
                            CollisionBoxSizeZ = reader.GetFloat("collision_box_size_z"),
                            CollisionSphereRadius = reader.GetFloat("collision_sphere_radius"),
                            Damaged25 = reader.GetString("damaged25"),
                            Damaged50 = reader.GetString("damaged50"),
                            Damaged75 = reader.GetString("damaged75"),
                            Dead = reader.GetString("dead"),
                            HaltRate = reader.GetFloat("halt_rate"),
                            ImpactMass = reader.GetFloat("impact_mass"),
                            MaxRpmSec = reader.GetFloat("max_rpm_sec"),
                            MinRpmSec = reader.GetFloat("min_rpm_sec"),
                            Normal = reader.GetString("normal"),
                            PassengerBoxOffsetX = reader.GetFloat("passenger_box_offset_x"),
                            PassengerBoxOffsetY = reader.GetFloat("passenger_box_offset_y"),
                            PassengerBoxOffsetZ = reader.GetFloat("passenger_box_offset_z"),
                            PassengerBoxScaleX = reader.GetFloat("passenger_box_scale_x"),
                            PassengerBoxScaleY = reader.GetFloat("passenger_box_scale_y"),
                            PassengerBoxScaleZ = reader.GetFloat("passenger_box_scale_z"),
                            WaterDamping = reader.GetFloat("water_damping")
                        };

                        _models["ShipModel"].TryAdd(model.Id, model);
                    }
                }
            }
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM vehicle_models";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var model = new VehicleModel
                        {
                            Id = reader.GetUInt32("id"),
                            LinInertia = reader.GetFloat("lin_inertia"),
                            LinDeaccelInertia = reader.GetFloat("lin_deaccel_inertia"),
                            RotInertia = reader.GetFloat("rot_inertia"),
                            RotDeaccelInertia = reader.GetFloat("rot_deaccel_inertia"),
                            Velocity = reader.GetFloat("velocity"),
                            AngVel = reader.GetFloat("angVel"),
                            CanFly = reader.GetBoolean("can_fly"),
                            WheeledVehicleMass = reader.GetFloat("wheeled_vehicle_mass"),
                            WheeledVehiclePower = reader.GetFloat("wheeled_vehicle_power"),
                            WheeledVehicleBrakeTorque = reader.GetFloat("wheeled_vehicle_brake_torque"),
                            WheeledVehicleMaxGear = reader.GetUInt32("wheeled_vehicle_max_gear"),
                            WheeledVehicleGearSpeedRatioReverse = reader.GetFloat("wheeled_vehicle_gear_speed_ratio_reverse"),
                            WheeledVehicleGearSpeedRatio1 = reader.GetFloat("wheeled_vehicle_gear_speed_ratio_1"),
                            WheeledVehicleGearSpeedRatio2 = reader.GetFloat("wheeled_vehicle_gear_speed_ratio_2"),
                            WheeledVehicleGearSpeedRatio3 = reader.GetFloat("wheeled_vehicle_gear_speed_ratio_3"),
                            WheeledVehicleSuspStroke = reader.GetFloat("wheeled_vehicle_susp_stroke"),
                            WheeledVehicleDrive = reader.GetInt32("wheeled_vehicle_drive"),
                            WheeledVehicleFrontOptimalSa = reader.GetFloat("wheeled_vehicle_front_optimal_sa"),
                            WheeledVehicleRearOptimalSa = reader.GetFloat("wheeled_vehicle_rear_optimal_sa"),
                            AutoLevel = reader.GetBoolean("auto_level"),
                            CharAnimSteerBackwardId = reader.GetInt32("char_anim_steer_backward_id"),
                            CharAnimSteerForwardId = reader.GetInt32("char_anim_steer_forward_id"),
                            CollisionBoxOffsetX = reader.GetFloat("collision_box_offset_x"),
                            CollisionBoxOffsetY = reader.GetFloat("collision_box_offset_y"),
                            CollisionBoxOffsetZ = reader.GetFloat("collision_box_offset_z"),
                            CollisionBoxScaleX = reader.GetFloat("collision_box_scale_x"),
                            CollisionBoxScaleY = reader.GetFloat("collision_box_scale_y"),
                            CollisionBoxScaleZ = reader.GetFloat("collision_box_scale_z"),
                            Damaged25 = reader.GetString("damaged25"),
                            Damaged50 = reader.GetString("damaged50"),
                            Damaged75 = reader.GetString("damaged75"),
                            Dead = reader.GetString("dead"),
                            DriverWalk = reader.GetBoolean("driver_walk"),
                            Dying = reader.GetString("dying"),
                            FloatingHeight = reader.GetFloat("floating_height"),
                            FloatingWaveHeight = reader.GetFloat("floating_wave_height"),
                            FloatingWavePeriodRatio = reader.GetFloat("floating_wave_period_ratio"),
                            InstalledTurret = reader.GetBoolean("installed_turret"),
                            MaxClimbAng = reader.GetFloat("max_climb_ang"),
                            Normal = reader.GetString("normal"),
                            SoundId = reader.GetInt32("sound_id"),
                            SuspAxle = reader.GetBoolean("susp_axle"),
                            SuspStroke = reader.GetFloat("susp_stroke"),
                            TrailAlignRatio = reader.GetFloat("trail_align_ratio"),
                            TurretPitchAngleMax = reader.GetFloat("turret_pitch_angle_max"),
                            TurretPitchAngleMin = reader.GetFloat("turret_pitch_angle_min"),
                            TurretPitchAngvel = reader.GetFloat("turret_pitch_angvel"),
                            TurretYawAngleMax = reader.GetFloat("turret_yaw_angle_max"),
                            TurretYawAngleMin = reader.GetFloat("turret_yaw_angle_min"),
                            TurretYawAngvel = reader.GetFloat("turret_yaw_angvel"),
                            UseCenterSpindle = reader.GetBoolean("use_center_spindle"),
                            UseProxyCollision = reader.GetBoolean("use_proxy_collision"),
                            UseWheeledVehicleSimulation = reader.GetBoolean("use_wheeled_vehicle_simulation"),
                            Wheel = reader.GetString("wheel"),
                            Wheel2 = reader.GetString("wheel2"),
                            WheeledVehicleBallastBoxX = reader.GetFloat("wheeled_vehicle_ballast_box_x"),
                            WheeledVehicleBallastBoxY = reader.GetFloat("wheeled_vehicle_ballast_box_y"),
                            WheeledVehicleBallastBoxZ = reader.GetFloat("wheeled_vehicle_ballast_box_z"),
                            WheeledVehicleBallastMass = reader.GetFloat("wheeled_vehicle_ballast_mass"),
                            WheeledVehicleBallastPosY = reader.GetFloat("wheeled_vehicle_ballast_pos_y"),
                            WheeledVehicleBallastPosZ = reader.GetFloat("wheeled_vehicle_ballast_pos_z"),
                            WheeledVehicleDespawnOnSpeedOver = reader.GetBoolean("wheeled_vehicle_despawn_on_speed_over"),
                            WheeledVehicleFrictionRatio = reader.GetFloat("wheeled_vehicle_friction_ratio"),
                            WheeledVehicleSpeedLimit = reader.GetFloat("wheeled_vehicle_speed_limit"),
                            WheeledVehicleSteer = reader.GetInt32("wheeled_vehicle_steer"),
                            WheeledVehicleSuspDamping = reader.GetFloat("wheeled_vehicle_susp_damping"),
                            WheeledVehicleSuspFrontAxisType = reader.GetInt32("wheeled_vehicle_susp_front_axis_type"),
                            WheeledVehicleSuspRearAxisType = reader.GetInt32("wheeled_vehicle_susp_rear_axis_type")
                        };

                        _models["VehicleModel"].TryAdd(model.Id, model);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM models";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var model = new ModelType
                        {
                            Id = reader.GetUInt32("id"),
                            SubId = reader.GetUInt32("sub_id"),
                            SubType = reader.GetString("sub_type"),
                            Big = reader.GetBoolean("big"),
                            CameraDistance = reader.GetFloat("camera_distance"),
                            CameraDistanceForWideAngle = reader.GetFloat("camera_distance_for_wide_angle"),
                            DespawnDoodadOnCollision = reader.GetBoolean("despawn_doodad_on_collision"),
                            DyingTime = reader.GetFloat("dying_time"),
                            HighImpactFxGroupId = reader.GetInt32("high_impact_fx_group_id"),
                            LowImpactFxGroupId = reader.GetInt32("low_impact_fx_group_id"),
                            MiddleImpactFxGroupId = reader.GetInt32("middle_impact_fx_group_id"),
                            MountPoseId = reader.GetInt32("mount_pose_id"),
                            Name = reader.GetString("name"),
                            NameTagOffset = reader.GetFloat("name_tag_offset"),
                            PlayMountAnimation = reader.GetBoolean("play_mount_animation"),
                            PlayerMountNameTagPos = reader.GetBoolean("player_mount_name_tag_pos"),
                            Selectable = reader.GetBoolean("selectable"),
                            ShowNameTag = reader.GetBoolean("show_name_tag"),
                            SoundMaterialId = reader.GetInt32("sound_material_id"),
                            SoundPackId = reader.GetInt32("sound_pack_id"),
                            TargetDecalSize = reader.GetFloat("target_decal_size"),
                            UseTargetDecal = reader.GetBoolean("use_target_decal"),
                            UseTargetHighlight = reader.GetBoolean("use_target_highlight"),
                            UseTargetSilhouette = reader.GetBoolean("use_target_silhouette")
                        };

                        _modelTypes.TryAdd(model.Id, model);
                    }
                }
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM game_stances";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var stance = new GameStance
                        {
                            Id = reader.GetUInt32("id"),
                            ActorModelId = reader.GetUInt32("actor_model_id"),
                            StanceId = (GameStanceType)(reader.GetByte("stance_id") - 1), // This seems to be +1 in the DB compared to the packets
                            Name = reader.GetString("name"),
                            AiMoveSpeedRun = reader.GetFloat("ai_move_speed_run"),
                            AiMoveSpeedSlow = reader.GetFloat("ai_move_speed_slow"),
                            AiMoveSpeedSprint = reader.GetFloat("ai_move_speed_sprint"),
                            AiMoveSpeedWalk = reader.GetFloat("ai_move_speed_walk"),
                            HeightCollider = reader.GetFloat("height_collider"),
                            HeightPivot = reader.GetFloat("height_pivot"),
                            IgnoreCollision = reader.GetBoolean("ignore_collision"),
                            MaxSpeed = reader.GetFloat("max_speed"),
                            ModelOffset = new Vector3(reader.GetFloat("model_offset_x"), reader.GetFloat("model_offset_y"), reader.GetFloat("model_offset_z")),
                            NormalSpeed = reader.GetFloat("normal_speed"),
                            Size = new Vector3(reader.GetFloat("size_x"), reader.GetFloat("size_y"), reader.GetFloat("size_z")),
                            UseCapsule = reader.GetBoolean("use_capsule", true),
                            ViewOffset = new Vector3(reader.GetFloat("view_offset_x"), reader.GetFloat("view_offset_y"), reader.GetFloat("view_offset_z")),
                        };

                        _gameStances.TryAdd(stance.Id, stance);
                        if (_models["ActorModel"].TryGetValue(stance.ActorModelId, out var m))
                        {
                            var actorModel = m as ActorModel;
                            if (actorModel != null)
                                actorModel.Stances.Add(stance.StanceId, stance);
                        }
                    }
                }
            }

        }

        _loaded = true;
    }
}
