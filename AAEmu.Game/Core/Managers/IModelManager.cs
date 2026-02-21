using AAEmu.Game.Models.Game.Models;

namespace AAEmu.Game.Core.Managers;

public interface IModelManager
{
    void Load();
    ModelType GetModelType(uint modelId);
    ActorModel GetActorModel(uint modelId);
    ShipModelV1 GetShipModel(uint modelId);
    VehicleModel GetVehicleModels(uint modelId);
    bool IsFlyOrSwim(uint modelId);
}
