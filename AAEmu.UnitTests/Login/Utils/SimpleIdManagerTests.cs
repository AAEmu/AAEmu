#nullable enable

using AAEmu.Login.Utils;

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

    [Test]
    public async Task Rent_FirstCall_ReturnsIdOne()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var entity = manager.Rent();

        // Assert
        await Assert.That(entity.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task Rent_MultipleCalls_ReturnsIncrementingIds()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var entity1 = manager.Rent();
        var entity2 = manager.Rent();
        var entity3 = manager.Rent();

        // Assert
        await Assert.That(entity1.Id).IsEqualTo(1u);
        await Assert.That(entity2.Id).IsEqualTo(2u);
        await Assert.That(entity3.Id).IsEqualTo(3u);
    }

    [Test]
    public async Task Return_ThenRent_ReusesReturnedId()
    {
        // Arrange
        var manager = CreateManager();
        var entity1 = manager.Rent(); // ID 1
        _ = manager.Rent(); // ID 2

        // Act
        manager.Return(entity1);
        var entity3 = manager.Rent();

        // Assert - should reuse ID 1, not allocate ID 3
        await Assert.That(entity3.Id).IsEqualTo(1u);
    }

    [Test]
    public async Task Return_MultipleReturns_ReusesInLifoOrder()
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

        await Assert.That(reused1.Id).IsEqualTo(3u);
        await Assert.That(reused2.Id).IsEqualTo(2u);
        await Assert.That(reused3.Id).IsEqualTo(1u);
    }

    [Test]
    public void Return_ZeroId_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var manager = CreateManager();
        var entityWithZeroId = new TestEntity(0);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Return(entityWithZeroId));
    }

    [Test]
    public async Task Rent_AfterReturnAndNewAllocations_ContinuesFromNextId()
    {
        // Arrange
        var manager = CreateManager();
        var entity1 = manager.Rent(); // ID 1
        manager.Return(entity1);
        var reused = manager.Rent(); // Reuses ID 1

        // Act - rent a new one, should be ID 2
        var entity2 = manager.Rent();

        // Assert
        await Assert.That(reused.Id).IsEqualTo(1u);
        await Assert.That(entity2.Id).IsEqualTo(2u);
    }

    [Test]
    public async Task Rent_ThreadSafety_AllIdsAreUnique()
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
        await Assert.That(uniqueIds).IsEqualTo(Count);
    }

    [Test]
    public async Task Return_ThreadSafety_AllReturnsSucceed()
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
        await Assert.That(maxReusedId).IsEqualTo(maxOriginalId);
    }
}