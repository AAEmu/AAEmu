namespace AAEmu.Game.Core.Managers.Id;

/// <summary>
/// Common interface for all ID managers.
/// </summary>
public interface IIdManager : ILoadable
{
    bool Initialize(bool forceReset = false);
    uint GetNextId();
    uint[] GetNextId(int count);
    void ReleaseId(uint usedObjectId);
    void ReleaseId(IEnumerable<uint> usedObjectIds);
}
