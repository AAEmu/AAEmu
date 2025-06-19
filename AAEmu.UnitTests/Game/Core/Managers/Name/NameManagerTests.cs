using AAEmu.Game.Core.Managers;
using AAEmu.Commons.Utils;
using Xunit;
using Moq;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers.Name;

public class NameManagerTests
{
    [Fact]
    public void EmptyNameManagerShouldNotHaveNames()
    {
        // Arrange
        var sut = NameManager.Instance;

        // Act
        sut.Load([], [], []);

        // Assert
        Assert.True(sut.NoNamesRegistered());
    }

    [Fact]
    public void AddCharacterNameShouldHaveNames()
    {
        // Arrange
        var charName = "TestName".NormalizeName();
        var charId = 1u;
        var charAccount = 1000u;

        var sut = NameManager.Instance;
        sut.Load([], [], []);

        // Act
        sut.AddCharacter(charId, charName, charAccount);

        // Assert
        Assert.False(sut.NoNamesRegistered());
        Assert.Equal(charName, sut.GetCharacterName(charId));
        Assert.Equal(charId, sut.GetCharacterId(charName));
    }

    [Fact]
    public void GetCharacterAccountShouldReturnFoundAccounts()
    {
        // Arrange
        var charId = 1u;
        var charAccount = 1000u;
        var sut = new NameManager();
        sut.Load([], [],
            characterAccounts: new Dictionary<uint, uint>
            {
                [charId] = charAccount
            });

        // Act
        var accountId = sut.GetCharacterAccount(charId);

        // Assert
        Assert.Equal(charAccount, accountId);
    }

    [Fact]
    public void ValidationCharacterNameAlreadyExistsCheck()
    {
        // Arrange
        var charId = 1u;
        var charAccount = 1000u;
        var charName = "TestName".NormalizeName();
        var mockCharacterManager = new Mock<CharacterManager>();
        mockCharacterManager.Setup(x => x.IsCharacterPendingDeletion(charName)).Returns(false);
        var sut = new NameManager(mockCharacterManager.Object);

        sut.Load([], [], []);

        sut.AddCharacter(charId, charName, charAccount);

        // Act
        var result = sut.ValidateCharacterName(charName);

        // Assert
        Assert.Equal(CharacterCreateError.NameAlreadyExists, result);
    }

    [Fact]
    public void ValidationCharacterNamePendingDeletionFailed()
    {
        // Arrange
        var charId = 1u;
        var charAccount = 1000u;
        var charName = "TestName".NormalizeName();
        var mockCharacterManager = new Mock<CharacterManager>();
        mockCharacterManager.Setup(x => x.IsCharacterPendingDeletion(charName)).Returns(true);
        var sut = new NameManager(mockCharacterManager.Object);

        sut.Load([], [], []);

        sut.AddCharacter(charId, charName, charAccount);

        // Act
        var result = sut.ValidateCharacterName(charName);

        // Assert
        Assert.Equal(CharacterCreateError.Failed, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("000&#$*")]
    public void ValidationCharacterInvalidName(string providedName)
    {
        // Arrange
        var charName = providedName.NormalizeName();
        var sut = new NameManager();

        sut.Load([], [], []);

        // Act
        var result = sut.ValidateCharacterName(charName);

        // Assert
        Assert.Equal(CharacterCreateError.InvalidCharacters, result);
    }

    [Theory]
    [InlineData("Roger")]
    [InlineData("Zero")]
    [InlineData("NLObP")]
    public void ValidationCharacterValidNameSucceed(string providedName)
    {
        // Arrange
        var charName = providedName.NormalizeName();
        var mockCharacterManager = new Mock<CharacterManager>();
        var sut = new NameManager(mockCharacterManager.Object);

        sut.Load([], [], []);

        // Act
        var result = sut.ValidateCharacterName(charName);

        // Assert
        Assert.Equal(CharacterCreateError.Ok, result);
    }

    [Fact]
    public void RemoveCharacterIdWorksAsExpected()
    {
        // Arrange
        var charId = 1u;
        var charAccount = 1000u;
        var charName = "TestName".NormalizeName();
        var mockCharacterManager = new Mock<CharacterManager>();
        mockCharacterManager.Setup(x => x.IsCharacterPendingDeletion(charName)).Returns(true);
        var sut = new NameManager(mockCharacterManager.Object);

        sut.Load([], [], []);

        sut.AddCharacter(charId, charName, charAccount);

        // Act
        sut.RemoveCharacterId(charId);

        // Assert
        Assert.True(sut.NoNamesRegistered());
        Assert.Null(sut.GetCharacterName(charId));
        Assert.Equal(0u, sut.GetCharacterId(charName));
        Assert.Equal(0u, sut.GetCharacterAccount(charId));
    }
}
