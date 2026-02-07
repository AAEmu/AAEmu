#nullable enable

using AAEmu.Login.Utils;
using Xunit;

namespace AAEmu.UnitTests.Login.Utils;

public class SimpleIdManagerTests
{
    private sealed record TestEntity(uint Id);

    private static SimpleIdManager<TestEntity> CreateManager()
    {
        return new SimpleIdManager<TestEntity>(
            factory: id => new TestEntity(id),
            accessor: entity => entity.Id);
    }

    [Fact]
    public void Rent_FirstCall_ReturnsIdOne()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var entity = manager.Rent();

        // Assert
        Assert.Equal(1u, entity.Id);
    }

    [Fact]
    public void Rent_MultipleCalls_ReturnsIncrementingIds()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var entity1 = manager.Rent();
        var entity2 = manager.Rent();
        var entity3 = manager.Rent();

        // Assert
        Assert.Equal(1u, entity1.Id);
        Assert.Equal(2u, entity2.Id);
        Assert.Equal(3u, entity3.Id);
    }

    [Fact]
    public void Return_ThenRent_ReusesReturnedId()
    {
        // Arrange
        var manager = CreateManager();
        var entity1 = manager.Rent(); // ID 1
        _ = manager.Rent(); // ID 2

        // Act
        manager.Return(entity1);
        var entity3 = manager.Rent();

        // Assert - should reuse ID 1, not allocate ID 3
        Assert.Equal(1u, entity3.Id);
    }

    [Fact]
    public void Return_MultipleReturns_ReusesInLifoOrder()
    {
        // Arrange
        var manager = CreateManager();
        var entity1 = manager.Rent(); // ID 1
        var entity2 = manager.Rent(); // ID 2
        var entity3 = manager.Rent(); // ID 3

        // Act - return in order 1, 2, 3
        manager.Return(entity1);
        manager.Return(entity2);
        manager.Return(entity3);

        // Assert - LIFO order: 3, 2, 1
        var reused1 = manager.Rent();
        var reused2 = manager.Rent();
        var reused3 = manager.Rent();

        Assert.Equal(3u, reused1.Id);
        Assert.Equal(2u, reused2.Id);
        Assert.Equal(1u, reused3.Id);
    }

    [Fact]
    public void Return_ZeroId_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var manager = CreateManager();
        var entityWithZeroId = new TestEntity(0);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Return(entityWithZeroId));
    }

    [Fact]
    public void Rent_AfterReturnAndNewAllocations_ContinuesFromNextId()
    {
        // Arrange
        var manager = CreateManager();
        var entity1 = manager.Rent(); // ID 1
        manager.Return(entity1);
        var reused = manager.Rent(); // Reuses ID 1

        // Act - rent a new one, should be ID 2
        var entity2 = manager.Rent();

        // Assert
        Assert.Equal(1u, reused.Id);
        Assert.Equal(2u, entity2.Id);
    }

    [Fact]
    public void Rent_ThreadSafety_AllIdsAreUnique()
    {
        // Arrange
        var manager = CreateManager();
        const int Count = 1000;
        var entities = new TestEntity[Count];

        // Act - rent in parallel
        Parallel.For(0, Count, i =>
        {
            entities[i] = manager.Rent();
        });

        // Assert - all IDs should be unique
        var uniqueIds = entities.Select(e => e.Id).Distinct().Count();
        Assert.Equal(Count, uniqueIds);
    }

    [Fact]
    public void Return_ThreadSafety_AllReturnsSucceed()
    {
        // Arrange
        var manager = CreateManager();
        const int Count = 1000;
        var entities = Enumerable.Range(0, Count).Select(_ => manager.Rent()).ToArray();

        // Act - return in parallel
        Parallel.ForEach(entities, entity =>
        {
            manager.Return(entity);
        });

        // Assert - renting again should reuse returned IDs
        var reusedEntities = Enumerable.Range(0, Count).Select(_ => manager.Rent()).ToArray();
        var maxOriginalId = entities.Max(e => e.Id);
        var maxReusedId = reusedEntities.Max(e => e.Id);

        // All reused IDs should be from the returned pool (no new allocations)
        Assert.Equal(maxOriginalId, maxReusedId);
    }
}
