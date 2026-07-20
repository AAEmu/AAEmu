using AAEmu.Game.Core.Managers;
using AAEmu.Commons.Utils;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers.Name;

public sealed class NameManagerTests
{
    [Test]
    public async Task EmptyNameManagerShouldNotHaveNames()
    {
        // Arrange
        var sut = new NameManager();

        // Act
        sut.Load([], [], []);

        // Assert
        await Assert.That(sut.NoNamesRegistered()).IsTrue();
    }

    [Test]
    public async Task AddCharacterNameShouldHaveNames()
    {
        // Arrange
        var charName = "TestName".NormalizeName();
        var charId = 1u;
        var charAccount = 1000u;

        var sut = new NameManager();
        sut.Load([], [], []);

        // Act
        sut.AddCharacter(charId, charName, charAccount);

        // Assert
        await Assert.That(sut.NoNamesRegistered()).IsFalse();
        await Assert.That(sut.GetCharacterName(charId)).IsEqualTo(charName);
        await Assert.That(sut.GetCharacterId(charName)).IsEqualTo(charId);
    }

    [Test]
    public async Task GetCharacterAccountShouldReturnFoundAccounts()
    {
        // Arrange
        var charId = 1u;
        var charAccount = 1000u;
        var sut = new NameManager();
        sut.Load([], [],
            characterAccounts: new Dictionary<uint, ulong>
            {
                [charId] = charAccount
            });

        // Act
        var accountId = sut.GetCharacterAccount(charId);

        // Assert
        await Assert.That(accountId).IsEqualTo(charAccount);
    }

    [Test]
    public async Task ValidationCharacterNameAlreadyExistsCheck()
    {
        // Arrange
        var charId = 1u;
        var charAccount = 1000u;
        var charName = "TestName".NormalizeName();
        var mockCharacterManager = Mock.Of<ICharacterManager>();
        mockCharacterManager.IsCharacterPendingDeletion(charName).Returns(false);
        var sut = new NameManager(new Lazy<ICharacterManager>(() => mockCharacterManager.Object));

        sut.Load([], [], []);

        sut.AddCharacter(charId, charName, charAccount);

        // Act
        var result = sut.ValidateCharacterName(charName);

        // Assert
        await Assert.That(result).IsEqualTo(CharacterCreateError.NameAlreadyExists);
    }

    [Test]
    public async Task ValidationCharacterNamePendingDeletionFailed()
    {
        // Arrange
        var charId = 1u;
        var charAccount = 1000u;
        var charName = "TestName".NormalizeName();
        var mockCharacterManager = Mock.Of<ICharacterManager>();
        mockCharacterManager.IsCharacterPendingDeletion(charName).Returns(true);
        var sut = new NameManager(new Lazy<ICharacterManager>(() => mockCharacterManager.Object));

        sut.Load([], [], []);

        sut.AddCharacter(charId, charName, charAccount);

        // Act
        var result = sut.ValidateCharacterName(charName);

        // Assert
        await Assert.That(result).IsEqualTo(CharacterCreateError.Failed);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("000&#$*")]
    public async Task ValidationCharacterInvalidName(string providedName)
    {
        // Arrange
        var charName = providedName.NormalizeName();
        var sut = new NameManager();

        sut.Load([], [], []);

        // Act
        var result = sut.ValidateCharacterName(charName);

        // Assert
        await Assert.That(result).IsEqualTo(CharacterCreateError.InvalidCharacters);
    }

    [Test]
    [Arguments("Roger")]
    [Arguments("Zero")]
    [Arguments("NLObP")]
    public async Task ValidationCharacterValidNameSucceed(string providedName)
    {
        // Arrange
        var charName = providedName.NormalizeName();
        var mockCharacterManager = Mock.Of<ICharacterManager>();
        var sut = new NameManager(new Lazy<ICharacterManager>(() => mockCharacterManager.Object));

        sut.Load([], [], []);

        // Act
        var result = sut.ValidateCharacterName(charName);

        // Assert
        await Assert.That(result).IsEqualTo(CharacterCreateError.Ok);
    }

    [Test]
    public async Task RemoveCharacterIdWorksAsExpected()
    {
        // Arrange
        var charId = 1u;
        var charAccount = 1000u;
        var charName = "TestName".NormalizeName();
        var mockCharacterManager = Mock.Of<ICharacterManager>();
        mockCharacterManager.IsCharacterPendingDeletion(charName).Returns(true);
        var sut = new NameManager(new Lazy<ICharacterManager>(() => mockCharacterManager.Object));

        sut.Load([], [], []);

        sut.AddCharacter(charId, charName, charAccount);

        // Act
        sut.RemoveCharacterId(charId);

        // Assert
        await Assert.That(sut.NoNamesRegistered()).IsTrue();
        await Assert.That(sut.GetCharacterName(charId)).IsNull();
        await Assert.That(sut.GetCharacterId(charName)).IsEqualTo(0u);
        await Assert.That(sut.GetCharacterAccount(charId)).IsEqualTo(0u);
    }
}