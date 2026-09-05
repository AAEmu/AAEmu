using AAEmu.Game.Core.Managers.Id;

namespace AAEmu.UnitTests.Utils.Mocks;

/// <summary>
/// Hands out mail ids in order from <see cref="FirstId"/> without touching MySQL, so a test
/// can predict which id a given letter will take.
/// </summary>
public sealed class SequentialMailIdManager : IMailIdManager
{
    public const uint FirstId = 10_000;

    private uint _next = FirstId;

    /// <summary>Id the next <see cref="GetNextId()"/> call will return.</summary>
    public uint Next => _next;

    public void Load()
    {
    }

    public bool Initialize(bool forceReset = false) => true;

    public uint GetNextId() => _next++;

    public uint[] GetNextId(int count)
    {
        var ids = new uint[count];
        for (var i = 0; i < count; i++)
            ids[i] = GetNextId();
        return ids;
    }

    public void ReleaseId(uint usedObjectId)
    {
    }

    public void ReleaseId(IEnumerable<uint> usedObjectIds)
    {
    }
}
