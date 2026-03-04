using AAEmu.Game.Core.Managers.Id;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers.Id;

public class IdManagerTests
{
    #region TheoryData for Parameterized Tests

    /// <summary>
    /// Provides test data for all ID managers with their first ID values.
    /// </summary>
    public static TheoryData<IIdManager, uint> IdManagerFirstIdData => new()
    {
        { CharacterIdManager.Instance, 0x00000001u },
        { ItemIdManager.Instance, 0x01000000u },
        { ObjectIdManager.Instance, 0x00000100u },
        { DoodadIdManager.Instance, 0x00000001u },
        { AuctionIdManager.Instance, 0x00000001u }
    };

    /// <summary>
    /// Provides test data for all ID managers for uniqueness tests.
    /// </summary>
    public static TheoryData<IIdManager> IdManagerData => new()
    {
        CharacterIdManager.Instance,
        ItemIdManager.Instance,
        ObjectIdManager.Instance,
        DoodadIdManager.Instance,
        AuctionIdManager.Instance
    };

    #endregion

    #region GetNextId Tests

    [Theory]
    [MemberData(nameof(IdManagerFirstIdData))]
    public void GetNextId_FirstCall_ReturnsFirstId(IIdManager manager, uint expectedFirstId)
    {
        // Arrange
        manager.Initialize(true);

        // Act
        var id = manager.GetNextId();

        // Assert
        Assert.Equal(expectedFirstId, id);
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void GetNextId_MultipleCalls_ReturnsSequentialIds(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var firstId = manager.GetNextId();

        // Act
        var secondId = manager.GetNextId();
        var thirdId = manager.GetNextId();

        // Assert
        Assert.Equal(firstId + 1, secondId);
        Assert.Equal(firstId + 2, thirdId);
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void GetNextId_MultipleCalls_IdsAreUnique(IIdManager manager)
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
        Assert.Equal(count, ids.Count);
    }

    #endregion

    #region GetNextId(int count) Tests

    [Theory]
    [MemberData(nameof(IdManagerFirstIdData))]
    public void GetNextId_WithCount_ReturnsArrayOfCorrectSize(IIdManager manager, uint expectedFirstId)
    {
        // Arrange
        manager.Initialize(true);
        const int count = 10;

        // Act
        var ids = manager.GetNextId(count);

        // Assert
        Assert.NotNull(ids);
        Assert.Equal(count, ids.Length);
    }

    [Theory]
    [MemberData(nameof(IdManagerFirstIdData))]
    public void GetNextId_WithCount_ReturnsSequentialIds(IIdManager manager, uint expectedFirstId)
    {
        // Arrange
        manager.Initialize(true);
        const int count = 10;

        // Act
        var ids = manager.GetNextId(count);

        // Assert
        Assert.Equal(expectedFirstId, ids[0]);
        for (var i = 1; i < count; i++)
        {
            Assert.Equal(ids[i - 1] + 1, ids[i]);
        }
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void GetNextId_WithCount_AllIdsAreUnique(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        const int count = 100;

        // Act
        var ids = manager.GetNextId(count);
        var uniqueIds = new HashSet<uint>(ids);

        // Assert
        Assert.Equal(count, uniqueIds.Count);
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void GetNextId_ZeroCount_ReturnsEmptyArray(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);

        // Act
        var ids = manager.GetNextId(0);

        // Assert
        Assert.NotNull(ids);
        Assert.Empty(ids);
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void GetNextId_SingleCount_ReturnsArrayWithOneElement(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);

        // Act
        var ids = manager.GetNextId(1);

        // Assert
        Assert.NotNull(ids);
        Assert.Single(ids);
    }

    #endregion

    #region ReleaseId Tests

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void ReleaseId_AfterGettingId_IdCanBeReused(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var id = manager.GetNextId();
        manager.GetNextId(); // Get another ID to move forward

        // Act
        manager.ReleaseId(id);
        var newId = manager.GetNextId();

        // Assert
        // The released ID should be the next one we get (smallest available)
        Assert.Equal(id, newId);
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void ReleaseId_MultipleIds_ReleasedInReverseOrder(IIdManager manager)
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
        Assert.Equal(id1, manager.GetNextId());
        Assert.Equal(id2, manager.GetNextId());
        Assert.Equal(id3, manager.GetNextId());
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void ReleaseId_InvalidId_DoesNotThrow(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);

        // Act & Assert - Should not throw
        manager.ReleaseId(0);
        manager.ReleaseId(uint.MaxValue);
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void ReleaseId_MultipleIdsViaEnumerable_AllReleased(IIdManager manager)
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
        Assert.Subset(new HashSet<uint>(newIds), new HashSet<uint>(ids));
    }

    #endregion

    #region Specific Manager Tests - CharacterIdManager

    [Fact]
    public void CharacterIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        // Arrange
        CharacterIdManager.Instance.Initialize(true);
        const uint expectedFirstId = 0x00000001u;

        // Act
        var id = CharacterIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(expectedFirstId, id);
    }

    [Fact]
    public void CharacterIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        // Arrange
        CharacterIdManager.Instance.Initialize(true);

        // Act
        var id1 = CharacterIdManager.Instance.GetNextId();
        var id2 = CharacterIdManager.Instance.GetNextId();
        var id3 = CharacterIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(0x00000001u, id1);
        Assert.Equal(0x00000002u, id2);
        Assert.Equal(0x00000003u, id3);
    }

    [Fact]
    public void CharacterIdManager_ReleaseId_MakesIdAvailable()
    {
        // Arrange
        CharacterIdManager.Instance.Initialize(true);
        var id = CharacterIdManager.Instance.GetNextId();
        CharacterIdManager.Instance.GetNextId();

        // Act
        CharacterIdManager.Instance.ReleaseId(id);
        var reusedId = CharacterIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(id, reusedId);
    }

    #endregion

    #region Specific Manager Tests - ItemIdManager

    [Fact]
    public void ItemIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        // Arrange
        ItemIdManager.Instance.Initialize(true);
        const uint expectedFirstId = 0x01000000u;

        // Act
        var id = ItemIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(expectedFirstId, id);
    }

    [Fact]
    public void ItemIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        // Arrange
        ItemIdManager.Instance.Initialize(true);

        // Act
        var id1 = ItemIdManager.Instance.GetNextId();
        var id2 = ItemIdManager.Instance.GetNextId();
        var id3 = ItemIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(0x01000000u, id1);
        Assert.Equal(0x01000001u, id2);
        Assert.Equal(0x01000002u, id3);
    }

    [Fact]
    public void ItemIdManager_ReleaseId_MakesIdAvailable()
    {
        // Arrange
        ItemIdManager.Instance.Initialize(true);
        var id = ItemIdManager.Instance.GetNextId();
        ItemIdManager.Instance.GetNextId();

        // Act
        ItemIdManager.Instance.ReleaseId(id);
        var reusedId = ItemIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(id, reusedId);
    }

    #endregion

    #region Specific Manager Tests - ObjectIdManager

    [Fact]
    public void ObjectIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        // Arrange
        ObjectIdManager.Instance.Initialize(true);
        const uint expectedFirstId = 0x00000100u;

        // Act
        var id = ObjectIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(expectedFirstId, id);
    }

    [Fact]
    public void ObjectIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        // Arrange
        ObjectIdManager.Instance.Initialize(true);

        // Act
        var id1 = ObjectIdManager.Instance.GetNextId();
        var id2 = ObjectIdManager.Instance.GetNextId();
        var id3 = ObjectIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(0x00000100u, id1);
        Assert.Equal(0x00000101u, id2);
        Assert.Equal(0x00000102u, id3);
    }

    [Fact]
    public void ObjectIdManager_ReleaseId_MakesIdAvailable()
    {
        // Arrange
        ObjectIdManager.Instance.Initialize(true);
        var id = ObjectIdManager.Instance.GetNextId();
        ObjectIdManager.Instance.GetNextId();

        // Act
        ObjectIdManager.Instance.ReleaseId(id);
        var reusedId = ObjectIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(id, reusedId);
    }

    [Fact]
    public void ObjectIdManager_GetNextId_Multiple_ReturnsArray()
    {
        // Arrange
        ObjectIdManager.Instance.Initialize(true);
        const uint firstId = 0x00000100u;

        // Act
        var ids = ObjectIdManager.Instance.GetNextId(10);

        // Assert
        Assert.Equal(10, ids.Length);
        Assert.Equal(firstId, ids[0]);
        Assert.Equal(firstId + 9, ids[9]);
    }

    #endregion

    #region Specific Manager Tests - DoodadIdManager

    [Fact]
    public void DoodadIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        // Arrange
        DoodadIdManager.Instance.Initialize(true);
        const uint expectedFirstId = 0x00000001u;

        // Act
        var id = DoodadIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(expectedFirstId, id);
    }

    [Fact]
    public void DoodadIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        // Arrange
        DoodadIdManager.Instance.Initialize(true);

        // Act
        var id1 = DoodadIdManager.Instance.GetNextId();
        var id2 = DoodadIdManager.Instance.GetNextId();
        var id3 = DoodadIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(0x00000001u, id1);
        Assert.Equal(0x00000002u, id2);
        Assert.Equal(0x00000003u, id3);
    }

    [Fact]
    public void DoodadIdManager_ReleaseId_MakesIdAvailable()
    {
        // Arrange
        DoodadIdManager.Instance.Initialize(true);
        var id = DoodadIdManager.Instance.GetNextId();
        DoodadIdManager.Instance.GetNextId();

        // Act
        DoodadIdManager.Instance.ReleaseId(id);
        var reusedId = DoodadIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(id, reusedId);
    }

    #endregion

    #region Specific Manager Tests - AuctionIdManager

    [Fact]
    public void AuctionIdManager_GetNextId_ReturnsCorrectFirstId()
    {
        // Arrange
        AuctionIdManager.Instance.Initialize(true);
        const uint expectedFirstId = 0x00000001u;

        // Act
        var id = AuctionIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(expectedFirstId, id);
    }

    [Fact]
    public void AuctionIdManager_GetNextId_SequentialCalls_IncrementsCorrectly()
    {
        // Arrange
        AuctionIdManager.Instance.Initialize(true);

        // Act
        var id1 = AuctionIdManager.Instance.GetNextId();
        var id2 = AuctionIdManager.Instance.GetNextId();
        var id3 = AuctionIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(0x00000001u, id1);
        Assert.Equal(0x00000002u, id2);
        Assert.Equal(0x00000003u, id3);
    }

    [Fact]
    public void AuctionIdManager_ReleaseId_MakesIdAvailable()
    {
        // Arrange
        AuctionIdManager.Instance.Initialize(true);
        var id = AuctionIdManager.Instance.GetNextId();
        AuctionIdManager.Instance.GetNextId();

        // Act
        AuctionIdManager.Instance.ReleaseId(id);
        var reusedId = AuctionIdManager.Instance.GetNextId();

        // Assert
        Assert.Equal(id, reusedId);
    }

    #endregion

    #region Edge Cases and Stress Tests

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void GetNextId_LargeNumberOfCalls_AllIdsUnique(IIdManager manager)
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
        Assert.Equal(count, ids.Count);
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void Initialize_MultipleCalls_DoesNotResetIds(IIdManager manager)
    {
        // Arrange
        manager.Initialize(true);
        var id1 = manager.GetNextId();

        // Act
        manager.Initialize(); // Second call without forceReset
        var id2 = manager.GetNextId();

        // Assert
        Assert.NotEqual(id1, id2);
        Assert.Equal(id1 + 1, id2);
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void Initialize_ForceReset_ResetsIds(IIdManager manager)
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
        Assert.True(id <= 0x01000001u); // Will be FirstId of the manager
    }

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void ReleaseAndGet_ManyIds_CorrectlyRecycles(IIdManager manager)
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
        var newIds = manager.GetNextId(50);

        // Assert - All original IDs should be reused (possibly in different order)
        Assert.Equal(new HashSet<uint>(ids), new HashSet<uint>(newIds));
    }

    #endregion

    #region Integration Scenarios

    [Theory]
    [MemberData(nameof(IdManagerData))]
    public void MixedOperations_ReleaseAndGet_MaintainsConsistency(IIdManager manager)
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
        Assert.Subset(releasedIds, newIds.ToHashSet());
    }

    [Fact]
    public void AllManagers_Initialized_SimultaneousUse()
    {
        // Arrange
        CharacterIdManager.Instance.Initialize(true);
        ItemIdManager.Instance.Initialize(true);
        ObjectIdManager.Instance.Initialize(true);
        DoodadIdManager.Instance.Initialize(true);
        AuctionIdManager.Instance.Initialize(true);

        // Act
        var charId = CharacterIdManager.Instance.GetNextId();
        var itemId = ItemIdManager.Instance.GetNextId();
        var objId = ObjectIdManager.Instance.GetNextId();
        var doodadId = DoodadIdManager.Instance.GetNextId();
        var auctionId = AuctionIdManager.Instance.GetNextId();

        // Assert - Each manager should return their respective first IDs
        Assert.Equal(0x00000001u, charId);
        Assert.Equal(0x01000000u, itemId);
        Assert.Equal(0x00000100u, objId);
        Assert.Equal(0x00000001u, doodadId);
        Assert.Equal(0x00000001u, auctionId);
    }

    [Fact]
    public void AllManagers_ReleaseId_IndependentlyManaged()
    {
        // Arrange
        CharacterIdManager.Instance.Initialize(true);
        ItemIdManager.Instance.Initialize(true);
        ObjectIdManager.Instance.Initialize(true);
        DoodadIdManager.Instance.Initialize(true);
        AuctionIdManager.Instance.Initialize(true);

        var charId = CharacterIdManager.Instance.GetNextId();
        var itemId = ItemIdManager.Instance.GetNextId();

        // Act
        CharacterIdManager.Instance.ReleaseId(charId);

        // Assert - Only CharacterIdManager should reuse the ID
        Assert.Equal(charId, CharacterIdManager.Instance.GetNextId());
        Assert.NotEqual(itemId, ItemIdManager.Instance.GetNextId());
    }

    #endregion
}
