
using AAEmu.Commons.Utils;

namespace AAEmu.UnitTests.Commons.Utils;

public class RandomElementByWeightTests
{
    private class TestItem
    {
        public string Name { get; set; } = "";
        public float Weight { get; set; }
    }

    [Test]
    public async Task RandomElementByWeight_ShouldReturnItem_WhenSequenceHasItems()
    {
        // Arrange
        var items = new List<TestItem>
        {
            new() { Name = "A", Weight = 1.0f },
            new() { Name = "B", Weight = 1.0f },
            new() { Name = "C", Weight = 1.0f }
        };

        // Act
        var result = items.RandomElementByWeight(i => i.Weight);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsNotEqualTo("");
    }

    [Test]
    public async Task RandomElementByWeight_ShouldReturnDefault_WhenSequenceIsEmpty()
    {
        // Arrange
        var items = new List<TestItem>();

        // Act
        var result = items.RandomElementByWeight(i => i.Weight);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task RandomElementByWeight_ShouldReturnOnlyItem_WhenSequenceHasOneItem()
    {
        // Arrange
        var items = new List<TestItem>
        {
            new() { Name = "OnlyOne", Weight = 10.0f }
        };

        // Act
        var result = items.RandomElementByWeight(i => i.Weight);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsEqualTo("OnlyOne");
    }

    [Test]
    public async Task RandomElementByWeight_ShouldConsiderWeights()
    {
        // Arrange
        var items = new List<TestItem>
        {
            new() { Name = "HighWeight", Weight = 100.0f },
            new() { Name = "LowWeight", Weight = 1.0f }
        };

        // Act - Run multiple times to test probabilistic behavior
        var highWeightCount = 0;
        for (var i = 0; i < 100; i++)
        {
            var result = items.RandomElementByWeight(j => j.Weight);
            if (result?.Name == "HighWeight")
                highWeightCount++;
        }

        // Assert - High weight item should appear significantly more often
        await Assert.That(highWeightCount > 50).IsTrue();
    }

    [Test]
    public async Task RandomElementByWeight_ShouldWorkWithZeroWeights()
    {
        // Arrange
        var items = new List<TestItem>
        {
            new() { Name = "ZeroWeight", Weight = 0.0f },
            new() { Name = "PositiveWeight", Weight = 10.0f }
        };

        // Act
        var result = items.RandomElementByWeight(i => i.Weight);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Name).IsEqualTo("PositiveWeight");
    }

    [Test]
    public async Task RandomElementByWeight_ShouldWorkWithAllSameWeights()
    {
        // Arrange
        var items = new List<TestItem>
        {
            new() { Name = "A", Weight = 5.0f },
            new() { Name = "B", Weight = 5.0f },
            new() { Name = "C", Weight = 5.0f }
        };

        // Act
        var results = new HashSet<string>();
        for (var i = 0; i < 50; i++)
        {
            var result = items.RandomElementByWeight(j => j.Weight);
            if (result != null)
                results.Add(result.Name);
        }

        // Assert - All items should appear at least once
        await Assert.That(results.Count).IsEqualTo(3);
    }

    [Test]
    public async Task RandomElementByWeight_ShouldWorkWithNullableTypes()
    {
        // Arrange
        var items = new List<string?> { "A", "B", "C" };

        // Act
        var result = items.RandomElementByWeight(_ => 1.0f);

        // Assert
        await Assert.That(result).IsNotNull();
    }
}