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
    /// <summary>
    /// True when the area sphere's trigger condition lets <paramref name="characterId"/> fire it now;
    /// records the firing. Claims are per character so open-world once/interval spheres are not
    /// shared across every player in the world.
    /// </summary>
    bool TryClaimAreaSphereTrigger(Spheres dbSphere, DateTime nowUtc, uint characterId);
    /// <summary>Undoes a claim whose firing did not happen.</summary>
    void ReleaseAreaSphereTrigger(Spheres dbSphere, DateTime claimedUtc, uint characterId);
    void Initialize();
    void Load();
    void RemoveSphereQuestTrigger(SphereQuestTrigger trigger);
}