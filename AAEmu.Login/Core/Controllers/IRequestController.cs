namespace AAEmu.Login.Core.Controllers;

public interface IRequestController
{
    (uint[] requestIds, Task result) Create(int count, int timeout);
    void ReleaseId(uint usedObjectId);
    void ReleaseId(IEnumerable<uint> usedObjectIds);
    bool Initialize();
    uint GetNextId();
    uint[] GetNextId(int count);
}
