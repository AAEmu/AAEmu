using Xunit;

using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class CompletedQuestTests
{
    [Fact]
    public void DefaultConstructor_ShouldInitializeEmpty()
    {
        // Arrange & Act
        var quest = new CompletedQuest();

        // Assert
        Assert.Equal(0, quest.Id);
        Assert.Null(quest.Body);
    }

    [Fact]
    public void ParameterizedConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var quest = new CompletedQuest(123);

        // Assert
        Assert.Equal(123, quest.Id);
        Assert.NotNull(quest.Body);
        Assert.Equal(64, quest.Body.Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(ushort.MaxValue)]
    public void ParameterizedConstructor_ShouldAcceptVariousIds(ushort id)
    {
        // Arrange & Act
        var quest = new CompletedQuest(id);

        // Assert
        Assert.Equal(id, quest.Id);
    }

    [Fact]
    public void Body_ShouldBeInitializedWith64Bits()
    {
        // Arrange
        var quest = new CompletedQuest(1);

        // Act & Assert
        Assert.Equal(64, quest.Body.Length);
    }

    [Fact]
    public void Properties_CanBeModified()
    {
        // Arrange
        var quest = new CompletedQuest();

        // Act
        quest.Id = 42;

        // Assert
        Assert.Equal(42, quest.Id);
    }
}
