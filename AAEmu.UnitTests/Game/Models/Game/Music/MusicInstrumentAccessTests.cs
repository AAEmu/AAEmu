using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.DoodadObj;
using AAEmu.Game.Models.Game.Housing;
using AAEmu.Game.Models.Game.Music;
using AAEmu.UnitTests.Utils.Mocks;

namespace AAEmu.UnitTests.Game.Models.Game.Music;

/// <summary>
/// Who may play through a placed instrument doodad: a world-spawned one for everyone, a
/// character-owned one only for its owner, a house-owned one through the house's own permission,
/// and a doodad whose house no longer resolves for nobody — the refusal has to be definitive.
/// </summary>
public class MusicInstrumentAccessTests
{
    private const uint OwnerId = 41;
    private const uint StrangerId = 42;

    private static Character Player(uint id) => new CharacterMock { Id = id, Name = $"Player{id}" };

    private static Doodad InstrumentAt(DoodadOwnerType ownerType, uint ownerId = 0, uint houseId = 0) =>
        new() { TemplateId = 90001, OwnerType = ownerType, OwnerId = ownerId, OwnerDbId = houseId };

    private static House HouseWith(bool alwaysPublic) => new()
    {
        OwnerId = OwnerId,
        Template = new HousingTemplate { AlwaysPublic = alwaysPublic },
        // Finished house: the unfinished branch of House.AllowedToInteract lets everyone in, which
        // would make the permission test below vacuous. The setter that spawns binding doodads is
        // skipped the way a database load skips it.
        IsBeingLoadedFromDb = true,
        CurrentStep = -1,
        Permission = alwaysPublic ? HousingPermission.Public : HousingPermission.Private,
    };

    [Test]
    public async Task AWorldSpawnedInstrumentIsPublic()
    {
        var instrument = InstrumentAt(DoodadOwnerType.System);

        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(StrangerId), instrument, null, 0)).IsTrue();
        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(OwnerId), instrument, null, 0)).IsTrue();
    }

    [Test]
    public async Task ACharacterOwnedInstrumentIsOnlyForItsOwner()
    {
        var instrument = InstrumentAt(DoodadOwnerType.Character, OwnerId);

        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(OwnerId), instrument, null, 0)).IsTrue();
        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(StrangerId), instrument, null, 0)).IsFalse();
    }

    [Test]
    public async Task AHouseOwnedInstrumentAsksTheHouse()
    {
        var instrument = InstrumentAt(DoodadOwnerType.Housing, OwnerId, houseId: 12);
        var privateHouse = HouseWith(alwaysPublic: false);
        var publicHouse = HouseWith(alwaysPublic: true);

        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(OwnerId), instrument, privateHouse, 0)).IsTrue();

        var stranger = Player(StrangerId);
        stranger.AccountId = 77; // NameManager knows no account for the owner, so this is not them.
        await Assert.That(MusicInstrumentAccess.MayPlayThrough(stranger, instrument, privateHouse, 0)).IsFalse();
        await Assert.That(MusicInstrumentAccess.MayPlayThrough(stranger, instrument, publicHouse, 0)).IsTrue();
    }

    [Test]
    public async Task AHouseThatNoLongerResolves_GrantsNobodyAnything()
    {
        var instrument = InstrumentAt(DoodadOwnerType.Housing, OwnerId, houseId: 12);

        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(OwnerId), instrument, null, 0)).IsFalse();
        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(StrangerId), instrument, null, 0)).IsFalse();
    }

    [Test]
    public async Task AHousingDoodadThatNamesNoHouse_GrantsNobodyAnything()
    {
        var instrument = InstrumentAt(DoodadOwnerType.Housing);

        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(OwnerId), instrument, null, 0)).IsFalse();
    }

    [Test]
    public async Task ASlaveMountedInstrumentIsOnlyForTheSlavesOwner()
    {
        // OwnerDbId is the slave, the same shape a house id has. It must not be asked as a house.
        var instrument = InstrumentAt(DoodadOwnerType.Slave, ownerId: 0, houseId: 12);

        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(OwnerId), instrument, null, OwnerId)).IsTrue();
        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(StrangerId), instrument, null, OwnerId)).IsFalse();
        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(OwnerId), instrument, HouseWith(true), 0)).IsFalse();
    }

    [Test]
    public async Task NoPlayerOrNoInstrument_IsNeverAllowed()
    {
        var instrument = InstrumentAt(DoodadOwnerType.System);

        await Assert.That(MusicInstrumentAccess.MayPlayThrough(null, instrument, null, 0)).IsFalse();
        await Assert.That(MusicInstrumentAccess.MayPlayThrough(Player(OwnerId), null, null, 0)).IsFalse();
    }
}
