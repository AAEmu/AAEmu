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

            if (!_models[modelType.SubType].ContainsKey(modelType.SubId))
                return null;
            
            return (ActorModel) _models[modelType.SubType][modelType.SubId];
        }
        
        public void Load()
        {
            _models = new Dictionary<string, Dictionary<uint, Model>>
            {
                {"ActorModel", new Dictionary<uint, Model>()},
                {"VehicleModel", new Dictionary<uint, Model>()},
                {"PrefabModel", new Dictionary<uint, Model>()}
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
