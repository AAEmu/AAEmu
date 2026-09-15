using System.Numerics;

using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers;

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

    /// <summary>
    /// True when the model id is a prefab - a siege place, a portal, a chest, a wall - rather than an
    /// actor. Those units are static props with no walk speed and no flight, which is worth knowing
    /// before an actor model is looked up and silently missed.
    /// </summary>
    public bool IsPrefabModel(uint modelId) =>
        _modelTypes.TryGetValue(modelId, out var modelType) &&
        modelType.SubType == nameof(PrefabModel) &&
        _models.TryGetValue(modelType.SubType, out var value) &&
        value.ContainsKey(modelType.SubId);

    public PrefabModel GetPrefabModel(uint modelId)
    {
        if (!_modelTypes.TryGetValue(modelId, out var modelType))
            return null;
        if (!_models.TryGetValue(modelType.SubType, out var value) || !value.TryGetValue(modelType.SubId, out var model))
            return null;
        return model as PrefabModel;
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

    /// <summary>
    /// True for a model the dedicate has to simulate off the ground - see
    /// <see cref="ActorModelRules.SimulatesOffGround"/>. This is what sets <c>Npc.CanFly</c>, which
    /// keeps a unit's spawn altitude and gets it a flying state on the zone.
    /// </summary>
    public bool IsFlyOrSwim(uint modelId) =>
        GetActorModel(modelId) is { } model && ActorModelRules.SimulatesOffGround(model);

    /// <summary>
    /// True for the swimmers among them. They are simulated off the ground like a flier but take a swim
    /// stance rather than the flight one, so the two cannot share a single flag.
    /// </summary>
    public bool IsSwimmer(uint modelId) =>
        GetActorModel(modelId) is { } model && ActorModelRules.SwimsUnderwater(model);

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
                            FlyMode = reader.GetBoolean("fly_mode", true),
                            UnderwaterCreature = reader.GetBoolean("underwater_creature", true)
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
                            WaterDensity = reader.GetFloat("water_density"),
                            WaterResistance = reader.GetFloat("water_resistance"),
                            SteerVel = reader.GetFloat("steer_vel"),
                            // 10.0.2.13: accel/reverse_accel removed (columns absent from ship_models)
                            ReverseVelocity = reader.GetFloat("reverse_velocity"),
                            TurnAccel = reader.GetFloat("turn_accel"),
                            TubeLength = reader.GetFloat("tube_length"),
                            TubeRadius = reader.GetFloat("tube_radius"),
                            TubeOffsetZ = reader.GetFloat("tube_offset_z"),
                            KeelLength = reader.GetFloat("keel_length"),
                            KeelHeight = reader.GetFloat("keel_height"),
                            KeelOffsetZ = reader.GetFloat("keel_offset_z")
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
                            // 10.0.2.13: velocity/wheeled_vehicle_power/wheeled_vehicle_brake_torque removed (columns absent from vehicle_models)
                            AngVel = reader.GetFloat("angVel"),
                            CanFly = reader.GetFloat("can_fly"),
                            WheeledVehicleMass = reader.GetFloat("wheeled_vehicle_mass"),
                            WheeledVehicleMaxGear = reader.GetUInt32("wheeled_vehicle_max_gear"),
                            WheeledVehicleGearSpeedRatioReverse = reader.GetFloat("wheeled_vehicle_gear_speed_ratio_reverse"),
                            WheeledVehicleGearSpeedRatio1 = reader.GetFloat("wheeled_vehicle_gear_speed_ratio_1"),
                            WheeledVehicleGearSpeedRatio2 = reader.GetFloat("wheeled_vehicle_gear_speed_ratio_2"),
                            WheeledVehicleGearSpeedRatio3 = reader.GetFloat("wheeled_vehicle_gear_speed_ratio_3"),
                            WheeledVehicleSuspStroke = reader.GetFloat("wheeled_vehicle_susp_stroke"),
                            WheeledVehicleDrive = reader.GetFloat("wheeled_vehicle_drive"),
                            WheeledVehicleFrontOptimalSa = reader.GetFloat("wheeled_vehicle_front_optimal_sa"),
                            WheeledVehicleRearOptimalSa = reader.GetFloat("wheeled_vehicle_rear_optimal_sa")
                        };

                        _models["VehicleModel"].TryAdd(model.Id, model);
                    }
                }
            }

            // prefab_models carries nothing but the id (the parts are in prefab_elements), and the
            // dictionary for it was set up and then never filled, so every prefab model id resolved to
            // nothing at all. The marker is what lets a unit be recognised as a static prop instead of
            // an actor whose model is missing.
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT * FROM prefab_models";
                command.Prepare();
                using (var reader = new SQLiteWrapperReader(command.ExecuteReader()))
                {
                    while (reader.Read())
                    {
                        var model = new PrefabModel { Id = reader.GetUInt32("id") };
                        _models["PrefabModel"].TryAdd(model.Id, model);
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
                            SubType = reader.GetString("sub_type")
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
                            MaxSpeed = reader.GetFloat("max_speed"),
                            ModelOffset = new Vector3(reader.GetFloat("model_offset_x"), reader.GetFloat("model_offset_y"), reader.GetFloat("model_offset_z")),
                            NormalSpeed = reader.GetFloat("normal_speed"),
                            Size = new Vector3(reader.GetFloat("size_x"), reader.GetFloat("size_y"), reader.GetFloat("size_z")),
                            UseCapsule = reader.GetBoolean("use_capsule"),
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
