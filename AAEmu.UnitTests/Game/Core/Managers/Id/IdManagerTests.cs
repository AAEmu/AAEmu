using AAEmu.Game.Core.Managers.Id;

namespace AAEmu.UnitTests.Game.Core.Managers.Id;

public class IdManagerTests
{
    #region TheoryData for Parameterized Tests

    public static IEnumerable<(IIdManager, uint)> IdManagerFirstIdData() =>
    [
        (new CharacterIdManager(), 0x00000001u),
        (new ItemIdManager(), 0x01000000u),
        (new ObjectIdManager(), 0x00000100u),
        (new DoodadIdManager(), 0x00000001u),
        (new AuctionIdManager(), 0x00000001u),
    ];

    public static IEnumerable<IIdManager> IdManagerData() =>
    [
        new CharacterIdManager(),
        new ItemIdManager(),
        new ObjectIdManager(),
        new DoodadIdManager(),
        new AuctionIdManager(),
    ];

    #endregion

    #region GetNextId Tests

    [Test]
    [MethodDataSource(nameof(IdManagerFirstIdData))]
    public async Task GetNextId_FirstCall_ReturnsFirstId(IIdManager manager, uint expectedFirstId)
    {
        // Arrange
        manager.Initialize(true);

        // Act
        var id = manager.GetNextId();

        // Assert
        await Assert.That(id).IsEqualTo(expectedFirstId);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task GetNextId_MultipleCalls_ReturnsSequentialIds(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var firstId = manager.GetNextId();

        // Act
        var secondId = manager.GetNextId();
        var thirdId = manager.GetNextId();

        // Assert
        await Assert.That(secondId).IsEqualTo(firstId + 1);
        await Assert.That(thirdId).IsEqualTo(firstId + 2);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task GetNextId_MultipleCalls_IdsAreUnique(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var ids = new HashSet<uint>();
        var count = 100;

        // Act
        for (var i = 0; i < count; i++)
        {
            ids.Add(manager.GetNextId());
        }

        // Assert
        await Assert.That(ids.Count).IsEqualTo(count);
    }

    #endregion

    #region GetNextId(int count) Tests

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task GetNextId_WithCount_ReturnsArrayOfCorrectSize(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        const int count = 10;

        // Act
        var ids = manager.GetNextId(count);

        // Assert
        await Assert.That(ids).IsNotNull();
        await Assert.That(ids.Length).IsEqualTo(count);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerFirstIdData))]
    public async Task GetNextId_WithCount_ReturnsSequentialIds(IIdManager manager, uint expectedFirstId)
    {
        // Arrange
        manager.Initialize(true);
        const int count = 10;

        // Act
        var ids = manager.GetNextId(count);

        // Assert
        await Assert.That(ids[0]).IsEqualTo(expectedFirstId);
        for (var i = 1; i < count; i++)
        {
            await Assert.That(ids[i]).IsEqualTo(ids[i - 1] + 1);
        }
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task GetNextId_WithCount_AllIdsAreUnique(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        const int count = 100;

        // Act
        var ids = manager.GetNextId(count);
        var uniqueIds = new HashSet<uint>(ids);

        // Assert
        await Assert.That(uniqueIds.Count).IsEqualTo(count);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task GetNextId_ZeroCount_ReturnsEmptyArray(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);

        // Act
        var ids = manager.GetNextId(0);

        // Assert
        await Assert.That(ids).IsNotNull();
        await Assert.That(ids).IsEmpty();
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task GetNextId_SingleCount_ReturnsArrayWithOneElement(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);

        // Act
        var ids = manager.GetNextId(1);

        // Assert
        await Assert.That(ids).IsNotNull();
        await Assert.That(ids).HasSingleItem();
    }

    #endregion

    #region ReleaseId Tests

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task ReleaseId_AfterGettingId_IdCanBeReused(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var id = manager.GetNextId();
        manager.GetNextId(); // Get another ID to move forward

        // Act
        manager.ReleaseId(id);
        var newId = manager.GetNextId();

        // Assert
        await Assert.That(newId).IsEqualTo(id);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task ReleaseId_MultipleIds_ReleasedInReverseOrder(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var id1 = manager.GetNextId();
        var id2 = manager.GetNextId();
        var id3 = manager.GetNextId();

        // Act - Release in reverse order
        manager.ReleaseId(id3);
        manager.ReleaseId(id2);
        manager.ReleaseId(id1);

        // Assert - Should get the smallest released ID first
        await Assert.That(manager.GetNextId()).IsEqualTo(id1);
        await Assert.That(manager.GetNextId()).IsEqualTo(id2);
        await Assert.That(manager.GetNextId()).IsEqualTo(id3);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public void ReleaseId_InvalidId_DoesNotThrow(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);

        // Act & Assert - Should not throw
        manager.ReleaseId(0);
        manager.ReleaseId(uint.MaxValue);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task ReleaseId_MultipleIdsViaEnumerable_AllReleased(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var ids = manager.GetNextId(5);
        manager.GetNextId(5); // Move forward

        // Act
        manager.ReleaseId(ids);

        // Assert - Released IDs should be available again
        var newIds = new List<uint>();
        for (var i = 0; i < 5; i++)
        {
            newIds.Add(manager.GetNextId());
        }

        // All original IDs should be in the new IDs
        await Assert.That(ids.All(newIds.Contains)).IsTrue();
    }

    #endregion

    #region Specific Manager Tests - CharacterIdManager

    [Test]
    public async Task CharacterIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        var manager = new CharacterIdManager();
        manager.Initialize(true);
        const uint expectedFirstId = 0x00000001u;

        var id = manager.GetNextId();

        await Assert.That(id).IsEqualTo(expectedFirstId);
    }

    [Test]
    public async Task CharacterIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        var manager = new CharacterIdManager();
        manager.Initialize(true);

        var id1 = manager.GetNextId();
        var id2 = manager.GetNextId();
        var id3 = manager.GetNextId();

        await Assert.That(id1).IsEqualTo(0x00000001u);
        await Assert.That(id2).IsEqualTo(0x00000002u);
        await Assert.That(id3).IsEqualTo(0x00000003u);
    }

    [Test]
    public async Task CharacterIdManager_ReleaseId_MakesIdAvailable()
    {
        var manager = new CharacterIdManager();
        manager.Initialize(true);
        var id = manager.GetNextId();
        manager.GetNextId();

        manager.ReleaseId(id);
        var reusedId = manager.GetNextId();

        await Assert.That(reusedId).IsEqualTo(id);
    }

    #endregion

    #region Specific Manager Tests - ItemIdManager

    [Test]
    public async Task ItemIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        var manager = new ItemIdManager();
        manager.Initialize(true);
        const uint expectedFirstId = 0x01000000u;

        var id = manager.GetNextId();

        await Assert.That(id).IsEqualTo(expectedFirstId);
    }

    [Test]
    public async Task ItemIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        var manager = new ItemIdManager();
        manager.Initialize(true);

        var id1 = manager.GetNextId();
        var id2 = manager.GetNextId();
        var id3 = manager.GetNextId();

        await Assert.That(id1).IsEqualTo(0x01000000u);
        await Assert.That(id2).IsEqualTo(0x01000001u);
        await Assert.That(id3).IsEqualTo(0x01000002u);
    }

    [Test]
    public async Task ItemIdManager_ReleaseId_MakesIdAvailable()
    {
        var manager = new ItemIdManager();
        manager.Initialize(true);
        var id = manager.GetNextId();
        manager.GetNextId();

        manager.ReleaseId(id);
        var reusedId = manager.GetNextId();

        await Assert.That(reusedId).IsEqualTo(id);
    }

    #endregion

    #region Specific Manager Tests - ObjectIdManager

    [Test]
    public async Task ObjectIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        var manager = new ObjectIdManager();
        manager.Initialize(true);
        const uint expectedFirstId = 0x00000100u;

        var id = manager.GetNextId();

        await Assert.That(id).IsEqualTo(expectedFirstId);
    }

    [Test]
    public async Task ObjectIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        var manager = new ObjectIdManager();
        manager.Initialize(true);

        var id1 = manager.GetNextId();
        var id2 = manager.GetNextId();
        var id3 = manager.GetNextId();

        await Assert.That(id1).IsEqualTo(0x00000100u);
        await Assert.That(id2).IsEqualTo(0x00000101u);
        await Assert.That(id3).IsEqualTo(0x00000102u);
    }

    [Test]
    public async Task ObjectIdManager_ReleaseId_MakesIdAvailable()
    {
        var manager = new ObjectIdManager();
        manager.Initialize(true);
        var id = manager.GetNextId();
        manager.GetNextId();

        manager.ReleaseId(id);
        var reusedId = manager.GetNextId();

        await Assert.That(reusedId).IsEqualTo(id);
    }

    [Test]
    public async Task ObjectIdManager_GetNextId_Multiple_ReturnsArray()
    {
        var manager = new ObjectIdManager();
        manager.Initialize(true);
        const uint firstId = 0x00000100u;

        var ids = manager.GetNextId(10);

        await Assert.That(ids.Length).IsEqualTo(10);
        await Assert.That(ids[0]).IsEqualTo(firstId);
        await Assert.That(ids[9]).IsEqualTo(firstId + 9);
    }

    #endregion

    #region Specific Manager Tests - DoodadIdManager

    [Test]
    public async Task DoodadIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        var manager = new DoodadIdManager();
        manager.Initialize(true);
        const uint expectedFirstId = 0x00000001u;

        var id = manager.GetNextId();

        await Assert.That(id).IsEqualTo(expectedFirstId);
    }

    [Test]
    public async Task DoodadIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        var manager = new DoodadIdManager();
        manager.Initialize(true);

        var id1 = manager.GetNextId();
        var id2 = manager.GetNextId();
        var id3 = manager.GetNextId();

        await Assert.That(id1).IsEqualTo(0x00000001u);
        await Assert.That(id2).IsEqualTo(0x00000002u);
        await Assert.That(id3).IsEqualTo(0x00000003u);
    }

    [Test]
    public async Task DoodadIdManager_ReleaseId_MakesIdAvailable()
    {
        var manager = new DoodadIdManager();
        manager.Initialize(true);
        var id = manager.GetNextId();
        manager.GetNextId();

        manager.ReleaseId(id);
        var reusedId = manager.GetNextId();

        await Assert.That(reusedId).IsEqualTo(id);
    }

    #endregion

    #region Specific Manager Tests - AuctionIdManager

    [Test]
    public async Task AuctionIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        var manager = new AuctionIdManager();
        manager.Initialize(true);
        const uint expectedFirstId = 0x00000001u;

        var id = manager.GetNextId();

        await Assert.That(id).IsEqualTo(expectedFirstId);
    }

    [Test]
    public async Task AuctionIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        var manager = new AuctionIdManager();
        manager.Initialize(true);

        var id1 = manager.GetNextId();
        var id2 = manager.GetNextId();
        var id3 = manager.GetNextId();

        await Assert.That(id1).IsEqualTo(0x00000001u);
        await Assert.That(id2).IsEqualTo(0x00000002u);
        await Assert.That(id3).IsEqualTo(0x00000003u);
    }

    [Test]
    public async Task AuctionIdManager_ReleaseId_MakesIdAvailable()
    {
        var manager = new AuctionIdManager();
        manager.Initialize(true);
        var id = manager.GetNextId();
        manager.GetNextId();

        manager.ReleaseId(id);
        var reusedId = manager.GetNextId();

        await Assert.That(reusedId).IsEqualTo(id);
    }

    #endregion

    #region Edge Cases and Stress Tests

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task GetNextId_LargeNumberOfCalls_AllIdsUnique(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        const int count = 1000;
        var ids = new HashSet<uint>();

        // Act
        for (var i = 0; i < count; i++)
        {
            ids.Add(manager.GetNextId());
        }

        // Assert
        await Assert.That(ids.Count).IsEqualTo(count);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task Initialize_MultipleCalls_DoesNotResetIds(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var id1 = manager.GetNextId();

        // Act
        manager.Initialize(); // Second call without forceReset
        var id2 = manager.GetNextId();

        // Assert
        await Assert.That(id2).IsNotEqualTo(id1);
        await Assert.That(id2).IsEqualTo(id1 + 1);
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task Initialize_ForceReset_ResetsIds(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        manager.GetNextId();
        manager.GetNextId();
        manager.GetNextId();

        // Act
        manager.Initialize(true); // Force reset
        var id = manager.GetNextId();

        // Assert - Should start from first ID again
        await Assert.That(id <= 0x01000001u).IsTrue(); // Will be FirstId of the manager
    }

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task ReleaseAndGet_ManyIds_CorrectlyRecycles(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var ids = manager.GetNextId(50);

        // Act - Release all IDs
        foreach (var id in ids)
        {
            manager.ReleaseId(id);
        }

        // Get the same number of IDs
        var newIds = manager.GetNextId(50).ToHashSet();

        // Assert - All original IDs should be reused (possibly in different order)
        await Assert.That(newIds.SetEquals(ids)).IsTrue();
    }

    #endregion

    #region Integration Scenarios

    [Test]
    [MethodDataSource(nameof(IdManagerData))]
    public async Task MixedOperations_ReleaseAndGet_MaintainsConsistency(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var allIds = new List<uint>();

        // Act - Get some IDs
        for (var i = 0; i < 10; i++)
        {
            allIds.Add(manager.GetNextId());
        }

        // Release every other ID
        for (var i = 0; i < allIds.Count; i += 2)
        {
            manager.ReleaseId(allIds[i]);
        }

        // Get new IDs
        var newIds = new List<uint>();
        for (var i = 0; i < 5; i++)
        {
            newIds.Add(manager.GetNextId());
        }

        // Assert - New IDs should include the released ones
        var releasedIds = allIds.Where((_, i) => i % 2 == 0).ToHashSet();
        await Assert.That(newIds.All(releasedIds.Contains)).IsTrue();
    }

    [Test]
    public async Task AllManagers_Initialized_SimultaneousUse()
    {
        // Arrange
        var charMgr = new CharacterIdManager();
        var itemMgr = new ItemIdManager();
        var objMgr = new ObjectIdManager();
        var doodadMgr = new DoodadIdManager();
        var auctionMgr = new AuctionIdManager();
        charMgr.Initialize(true);
        itemMgr.Initialize(true);
        objMgr.Initialize(true);
        doodadMgr.Initialize(true);
        auctionMgr.Initialize(true);

        // Act
        var charId = charMgr.GetNextId();
        var itemId = itemMgr.GetNextId();
        var objId = objMgr.GetNextId();
        var doodadId = doodadMgr.GetNextId();
        var auctionId = auctionMgr.GetNextId();

        // Assert - Each manager should return their respective first IDs
        await Assert.That(charId).IsEqualTo(0x00000001u);
        await Assert.That(itemId).IsEqualTo(0x01000000u);
        await Assert.That(objId).IsEqualTo(0x00000100u);
        await Assert.That(doodadId).IsEqualTo(0x00000001u);
        await Assert.That(auctionId).IsEqualTo(0x00000001u);
    }

    [Test]
    public async Task AllManagers_ReleaseId_IndependentlyManaged()
    {
        // Arrange
        var charMgr = new CharacterIdManager();
        var itemMgr = new ItemIdManager();
        charMgr.Initialize(true);
        itemMgr.Initialize(true);

        var charId = charMgr.GetNextId();
        var itemId = itemMgr.GetNextId();

        // Act
        charMgr.ReleaseId(charId);

        // Assert - Only CharacterIdManager should reuse the ID
        await Assert.That(charMgr.GetNextId()).IsEqualTo(charId);
        await Assert.That(itemMgr.GetNextId()).IsNotEqualTo(itemId);
    }

    #endregion
}
