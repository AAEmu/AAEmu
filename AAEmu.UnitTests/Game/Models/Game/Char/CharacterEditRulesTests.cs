using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Items;

namespace AAEmu.UnitTests.Game.Models.Game.Char;

public class CharacterEditRulesTests
{
    private sealed class Catalog : ICharacterCustomizationCatalog
    {
        public HashSet<(uint, EquipmentItemSlot, uint)> BodyPartItems { get; } = [];

        public bool TryGetBodyPart(uint modelId, uint itemId, out CustomizingBodyPart part)
        {
            part = default;
            return false;
        }

        public bool IsBodyPartItem(uint modelId, EquipmentItemSlot slot, uint itemId) => BodyPartItems.Contains((modelId, slot, itemId));
        public bool IsColor(CustomizingCategory category, uint colorId) => false;
        public bool IsSkinColor(uint modelId, uint id) => false;
        public bool IsFaceNormalMap(uint modelId, uint id) => false;
        public bool IsFaceDiffuseMap(uint modelId, uint id) => false;
        public bool IsFaceEyelashMap(uint modelId, uint id) => false;
        public bool IsBodyNormalMap(uint modelId, uint id) => false;
        public bool IsBodyDiffuseMap(uint modelId, uint id) => false;
        public bool TryGetFaceDecal(uint id, out FaceDecalAsset decal)
        {
            decal = default;
            return false;
        }
    }

    private static CharacterEditError Validate(
        bool periodOpen = true, bool inLobby = true, bool owns = true, bool sameName = true,
        bool sameRace = true, bool sameGender = true, bool sameLevel = true, bool sameAbilities = true) =>
        CharacterEditRules.Validate(periodOpen, inLobby, owns, sameName, sameRace, sameGender, sameLevel, sameAbilities);

    [Test]
    public async Task PreSelectCharacterPeriod_IsNotPublished()
    {
        // The value SCInitialConfigPacket writes as "enable"; nothing opens the lobby edit until it changes.
        await Assert.That(CharacterEditRules.PreSelectCharacterPeriod).IsFalse();
        await Assert.That(Validate(periodOpen: CharacterEditRules.PreSelectCharacterPeriod)).IsEqualTo(CharacterEditError.PeriodClosed);
    }

    [Test]
    public async Task Validate_AllGatesPassInsideThePeriod()
    {
        await Assert.That(Validate()).IsEqualTo(CharacterEditError.None);
    }

    [Test]
    public async Task Validate_ClosedPeriodWinsOverEverything()
    {
        await Assert.That(Validate(periodOpen: false, inLobby: false, owns: false)).IsEqualTo(CharacterEditError.PeriodClosed);
    }

    [Test]
    public async Task Validate_MustBeInTheLobbyAndOwnTheCharacter()
    {
        await Assert.That(Validate(inLobby: false)).IsEqualTo(CharacterEditError.NotInLobby);
        await Assert.That(Validate(owns: false)).IsEqualTo(CharacterEditError.NotOwned);
    }

    [Test]
    public async Task Validate_IdentityFieldsMayNotChange()
    {
        await Assert.That(Validate(sameName: false)).IsEqualTo(CharacterEditError.IdentityChanged);
        await Assert.That(Validate(sameRace: false)).IsEqualTo(CharacterEditError.IdentityChanged);
        await Assert.That(Validate(sameGender: false)).IsEqualTo(CharacterEditError.IdentityChanged);
        await Assert.That(Validate(sameLevel: false)).IsEqualTo(CharacterEditError.IdentityChanged);
        await Assert.That(Validate(sameAbilities: false)).IsEqualTo(CharacterEditError.IdentityChanged);
    }

    [Test]
    public async Task ValidateBodyItems_SevenSlotsFaceToBeard()
    {
        await Assert.That(CharacterEditRules.BodyItemCount).IsEqualTo(7);
        await Assert.That(CharacterEditRules.FirstBodySlot).IsEqualTo(EquipmentItemSlot.Face);
        await Assert.That(CharacterEditRules.BodySlot(0)).IsEqualTo(EquipmentItemSlot.Face);
        await Assert.That(CharacterEditRules.BodySlot(1)).IsEqualTo(EquipmentItemSlot.Hair);
        await Assert.That(CharacterEditRules.BodySlot(6)).IsEqualTo(EquipmentItemSlot.Beard);
    }

    [Test]
    public async Task ValidateBodyItems_ZeroKeepsTheSlot()
    {
        var catalog = new Catalog();
        await Assert.That(CharacterEditRules.ValidateBodyItems(catalog, 10, new uint[7])).IsEqualTo(CharacterEditError.None);
    }

    [Test]
    public async Task ValidateBodyItems_EachItemMustBeARowForTheModelAndSlot()
    {
        var catalog = new Catalog();
        catalog.BodyPartItems.Add((10, EquipmentItemSlot.Hair, 24127));
        catalog.BodyPartItems.Add((10, EquipmentItemSlot.Face, 25086));

        var items = new uint[7];
        items[1] = 24127; // hair is index 1 (slot 20)
        items[0] = 25086; // face is index 0 (slot 19)
        await Assert.That(CharacterEditRules.ValidateBodyItems(catalog, 10, items)).IsEqualTo(CharacterEditError.None);

        items[2] = 24127; // the hair item in the glasses slot
        await Assert.That(CharacterEditRules.ValidateBodyItems(catalog, 10, items)).IsEqualTo(CharacterEditError.UnknownBodyItem);

        items[2] = 0;
        await Assert.That(CharacterEditRules.ValidateBodyItems(catalog, 11, items)).IsEqualTo(CharacterEditError.UnknownBodyItem);
    }

    [Test]
    public async Task ValidateBodyItems_WrongLengthIsRefused()
    {
        var catalog = new Catalog();
        await Assert.That(CharacterEditRules.ValidateBodyItems(catalog, 10, null)).IsEqualTo(CharacterEditError.UnknownBodyItem);
        await Assert.That(CharacterEditRules.ValidateBodyItems(catalog, 10, new uint[6])).IsEqualTo(CharacterEditError.UnknownBodyItem);
    }
}
