using System.Numerics;
using AAEmu.Game.Models.Game.World;
using AAEmu.Game.Models.Spheres;

namespace AAEmu.Game.Core.Managers.World;

public interface ISphereQuestManager
{
    void AddSphereQuestTrigger(SphereQuestTrigger trigger);
    List<SphereQuest> GetQuestSpheres(uint componentId);
    List<SphereQuestTrigger> GetSphereQuestTriggers();
    /// <summary>quest_area_sphere.g volumes whose stype is spheres.id and that contain worldPos.</summary>
    IReadOnlyList<SphereQuest> GetContainingQuestAreaSpheres(uint zoneId, Vector3 worldPos);
    /// <summary>True when the area sphere's trigger condition lets it fire in this world instance now; records the firing.</summary>
    bool TryClaimAreaSphereTrigger(Spheres dbSphere, DateTime nowUtc);
    /// <summary>Undoes a claim whose firing did not happen.</summary>
    void ReleaseAreaSphereTrigger(Spheres dbSphere, DateTime claimedUtc);
    void Initialize();
    void Load();
    void RemoveSphereQuestTrigger(SphereQuestTrigger trigger);
}