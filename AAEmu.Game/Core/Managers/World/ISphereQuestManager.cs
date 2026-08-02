using System.Numerics;
using AAEmu.Game.Models.Game.World;

namespace AAEmu.Game.Core.Managers.World;

public interface ISphereQuestManager
{
    void AddSphereQuestTrigger(SphereQuestTrigger trigger);
    List<SphereQuest> GetQuestSpheres(uint componentId);
    List<SphereQuestTrigger> GetSphereQuestTriggers();
    /// <summary>quest_area_sphere.g volumes whose stype is spheres.id and that contain worldPos.</summary>
    IReadOnlyList<SphereQuest> GetContainingQuestAreaSpheres(uint zoneId, Vector3 worldPos);
    void Initialize();
    void Load();
    void RemoveSphereQuestTrigger(SphereQuestTrigger trigger);
}