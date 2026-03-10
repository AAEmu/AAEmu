using AAEmu.Game.Models.Game.Quests;

namespace AAEmu.UnitTests.Game.Models.Game.Quests;

public class CompletedQuestTests
{
    [Test]
    public async Task DefaultConstructor_ShouldInitializeEmpty()
    {
        // Arrange & Act
        var quest = new CompletedQuest();

        // Assert
        await Assert.That(quest.Id).IsEqualTo((ushort)0);
        await Assert.That(quest.Body).IsNull();
    }

    [Test]
    public async Task ParameterizedConstructor_ShouldInitializeProperties()
    {
        // Arrange & Act
        var quest = new CompletedQuest(123);

        // Assert
        await Assert.That(quest.Id).IsEqualTo((ushort)123);
        await Assert.That(quest.Body).IsNotNull();
        await Assert.That(quest.Body.Length).IsEqualTo(64);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(100)]
    [Arguments(ushort.MaxValue)]
    public async Task ParameterizedConstructor_ShouldAcceptVariousIds(ushort id)
    {
        // Arrange & Act
        var quest = new CompletedQuest(id);

        // Assert
        await Assert.That(quest.Id).IsEqualTo(id);
    }

    [Test]
    public async Task Body_ShouldBeInitializedWith64Bits()
    {
        // Arrange
        var quest = new CompletedQuest(1);

        // Act & Assert
        await Assert.That(quest.Body.Length).IsEqualTo(64);
    }

    [Test]
    public async Task Properties_CanBeModified()
    {
        // Arrange
        var quest = new CompletedQuest();

        // Act
        quest.Id = 42;

        // Assert
        await Assert.That(quest.Id).IsEqualTo((ushort)42);
    }
}
