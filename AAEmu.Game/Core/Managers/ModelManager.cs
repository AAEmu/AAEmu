using System.Collections.Generic;
using AAEmu.Commons.Utils;
using AAEmu.Game.Models.Game.Models;
using AAEmu.Game.Utils.DB;

namespace AAEmu.Game.Core.Managers
{
    namespace AAEmu.Game.Core.Managers
{
    public class ModelManager : Singleton<ModelManager>
    {

        private Dictionary<string, Dictionary<uint, Model>> _models;
        private Dictionary<uint, ModelType> _modelTypes;

        // Getters
        public ActorModel GetActorModel(uint modelId)
        {
            if (!_modelTypes.ContainsKey(modelId))
                return null;
            
            var modelType = _modelTypes[modelId];

            if (!_models.ContainsKey(modelType.SubType) || !_models[modelType.SubType].ContainsKey(modelType.SubId))
                return null;
            
            return (ActorModel) _models[modelType.SubType][modelType.SubId];
        }
        
        public ShipModel GetShipModel(uint modelId)
        {
            if (!_modelTypes.ContainsKey(modelId))
                return null;
            
            var modelType = _modelTypes[modelId];

            if (!_models.ContainsKey(modelType.SubType) || !_models[modelType.SubType].ContainsKey(modelType.SubId))
                return null;
            
            return (ShipModel) _models[modelType.SubType][modelType.SubId];
        }
        
        public void Load()
        {
            _models = new Dictionary<string, Dictionary<uint, Model>>
            {
                {"ActorModel", new Dictionary<uint, Model>()},
                {"VehicleModel", new Dictionary<uint, Model>()},
                {"PrefabModel", new Dictionary<uint, Model>()},
                {"ShipModel", new Dictionary<uint, Model>()}
            };
            
            _modelTypes = new Dictionary<uint, ModelType>();

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
                            var model = new ActorModel()
                            {
                                Id = reader.GetUInt32("id"),
                                Radius = reader.GetFloat("radius"),
                                Height = reader.GetFloat("height")
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
                            var model = new ShipModel()
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
                                SteerVel = reader.GetFloat("steer_vel"),
                                Accel = reader.GetFloat("accel"),
                                ReverseAccel = reader.GetFloat("reverse_accel"),
                                ReverseVelocity = reader.GetFloat("reverse_velocity"),
                                TurnAccel = reader.GetFloat("turn_accel")
                            };

                            _models["ShipModel"].TryAdd(model.Id, model);
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
                            var model = new ModelType()
                            {
                                Id = reader.GetUInt32("id"),
                                SubId = reader.GetUInt32("sub_id"),
                                SubType = reader.GetString("sub_type")
                            };

                            _modelTypes.TryAdd(model.Id, model);
                        }
                    }
                }
            }
        }
    }
}

}
