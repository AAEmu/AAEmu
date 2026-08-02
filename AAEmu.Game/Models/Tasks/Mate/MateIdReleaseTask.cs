using AAEmu.Game.Core.Managers.Id;

namespace AAEmu.Game.Models.Tasks.Mate;

/// <summary>
/// Returns a despawned mount's object and tl ids to their pools once the client has retired the unit.
/// </summary>
/// <remarks>
/// The client holds five mount movement handlers in a fixed table (InputManager::SetPetUnit,
/// unit's object id, preferring the slot whose key already matches over an unused one. Releasing the id
/// as part of the despawn let the next summon draw the same value and bind to the previous mount's slot,
/// carrying its movement state into the new unit. Holding the id back keeps the two summons distinct.
/// </remarks>
public class MateIdReleaseTask(uint objId, ushort tlId) : Task
{
    public override void Execute()
    {
        ObjectIdManager.Instance.ReleaseId(objId);
        TlIdManager.Instance.ReleaseId(tlId);
    }
}
