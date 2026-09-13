using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Butlers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Core.Managers;

public sealed class ButlerRenameServiceTests
{
    [Test]
    public async Task Rename_PersistsCompleteProjectionBeforeChangingLiveName()
    {
        const uint characterId = 42;
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var butler = new CharacterButler(characterId);
        butler.Apply(new CharacterButlerRecord(characterId, 77, "Oldname", 123, 4, 56, 789));
        var manager = Mock.Of<IButlerManager>();
        manager.GetOrCreate(characterId).Returns(butler);
        CharacterButlerRecord persisted = default;
        string liveNameDuringPersistence = null;
        string liveNameDuringPublication = null;
        var service = new ButlerRenameService(manager.Object, record =>
        {
            persisted = record;
            liveNameDuringPersistence = butler.Name;
        }, (_, _) => liveNameDuringPublication = butler.Name);

        var result = service.Rename(character, "newNAME");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(result.Name).IsEqualTo("newNAME");
        await Assert.That(liveNameDuringPersistence).IsEqualTo("Oldname");
        await Assert.That(liveNameDuringPublication).IsEqualTo("newNAME");
        await Assert.That(persisted).IsEqualTo(
            new CharacterButlerRecord(characterId, 77, "newNAME", 123, 4, 56, 789));
        await Assert.That(butler.Name).IsEqualTo("newNAME");
    }

    [Test]
    public async Task Rename_PersistenceFailurePreservesLiveName()
    {
        const uint characterId = 42;
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var butler = new CharacterButler(characterId) { HouseId = 77, Name = "Oldname" };
        var manager = Mock.Of<IButlerManager>();
        manager.GetOrCreate(characterId).Returns(butler);
        var publicationCalls = 0;
        var service = new ButlerRenameService(
            manager.Object,
            _ => throw new InvalidOperationException("database unavailable"),
            (_, _) => publicationCalls++);

        var result = service.Rename(character, "Newname");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ButlerRenameFailure.PersistenceFailed);
        await Assert.That(butler.Name).IsEqualTo("Oldname");
        await Assert.That(publicationCalls).IsEqualTo(0);
    }

    [Test]
    public async Task Rename_SameNameAcknowledgesWithoutWritingDatabase()
    {
        const uint characterId = 42;
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var butler = new CharacterButler(characterId) { HouseId = 77, Name = "SameName" };
        var manager = Mock.Of<IButlerManager>();
        manager.GetOrCreate(characterId).Returns(butler);
        var persistenceCalls = 0;
        var publicationCalls = 0;
        var service = new ButlerRenameService(
            manager.Object,
            _ => persistenceCalls++,
            (_, name) => publicationCalls += name == "SameName" ? 1 : 100);

        var result = service.Rename(character, "SameName");

        await Assert.That(result.Success).IsTrue();
        await Assert.That(persistenceCalls).IsEqualTo(0);
        await Assert.That(publicationCalls).IsEqualTo(1);
    }

    [Test]
    public async Task Rename_RequiresCurrentCharactersBoundFarmhand()
    {
        const uint characterId = 42;
        var character = new Character(new UnitCustomModelParams()) { Id = characterId };
        var butler = new CharacterButler(characterId) { Name = "Oldname" };
        var manager = Mock.Of<IButlerManager>();
        manager.GetOrCreate(characterId).Returns(butler);
        var persistenceCalls = 0;
        var service = new ButlerRenameService(manager.Object, _ => persistenceCalls++);

        var result = service.Rename(character, "Newname");

        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Failure).IsEqualTo(ButlerRenameFailure.NotBound);
        await Assert.That(persistenceCalls).IsEqualTo(0);
        await Assert.That(butler.Name).IsEqualTo("Oldname");
    }

    [Test]
    [Arguments("A")]
    [Arguments("TwentySixCharacterNameLong")]
    [Arguments("Has Space")]
    [Arguments("Bad-Name")]
    [Arguments("Садовник")]
    public async Task SummonsPolicy_RejectsInvalidStructuralNames(string requestedName)
    {
        var valid = ButlerRenameService.TryNormalizeName(requestedName, out _);

        await Assert.That(valid).IsFalse();
    }

    [Test]
    public async Task SummonsPolicy_AcceptsUiMaximumAndPreservesCase()
    {
        var requestedName = new string('a', ButlerRenameService.MaximumCharacterCount);

        var valid = ButlerRenameService.TryNormalizeName(requestedName, out var normalized);

        await Assert.That(valid).IsTrue();
        await Assert.That(normalized).IsEqualTo(requestedName);
    }

    [Test]
    public async Task SummonsPolicy_RejectsMalformedUtf16()
    {
        var malformed = "Ok\uD800";

        var valid = ButlerRenameService.TryNormalizeName(malformed, out _);

        await Assert.That(valid).IsFalse();
    }
}
