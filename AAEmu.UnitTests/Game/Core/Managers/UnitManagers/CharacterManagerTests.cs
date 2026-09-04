using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Managers.UnitManagers;
using AAEmu.Game.Core.Managers.World;
using AAEmu.Game.Models.Account;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Char.Templates;
using AAEmu.Game.Models.Game.Items;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Models.StaticValues;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public class CharacterManagerTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_DoesNotCallDependencies()
    {
        // Arrange
        var mockWorldManager = Mock.Of<IWorldManager>();
        var mockAccountManager = Mock.Of<IAccountManager>();
        var mockNameManager = Mock.Of<INameManager>();
        var mockCharacterIdManager = Mock.Of<ICharacterIdManager>();
        var mockFactionManager = Mock.Of<IFactionManager>();
        var mockSkillManager = Mock.Of<ISkillManager>();
        var mockItemManager = Mock.Of<IItemManager>();
        var mockHousingManager = Mock.Of<IHousingManager>();
        var mockFamilyManager = Mock.Of<IFamilyManager>();
        var mockMailManager = Mock.Of<IMailManager>();
        var mockTaskManager = Mock.Of<ITaskManager>();

        // Act
        var manager = new CharacterManager(
            mockWorldManager.Object,
            mockAccountManager.Object,
            mockNameManager.Object,
            mockCharacterIdManager.Object,
            mockFactionManager.Object,
            mockSkillManager.Object,
            mockItemManager.Object,
            mockHousingManager.Object,
            mockFamilyManager.Object,
            mockMailManager.Object,
            mockTaskManager.Object);

        // Assert
        await Assert.That(manager).IsNotNull();
        Mock.VerifyNoOtherCalls(mockWorldManager);
        Mock.VerifyNoOtherCalls(mockAccountManager);
        Mock.VerifyNoOtherCalls(mockNameManager);
        Mock.VerifyNoOtherCalls(mockCharacterIdManager);
        Mock.VerifyNoOtherCalls(mockFactionManager);
        Mock.VerifyNoOtherCalls(mockSkillManager);
        Mock.VerifyNoOtherCalls(mockItemManager);
        Mock.VerifyNoOtherCalls(mockHousingManager);
        Mock.VerifyNoOtherCalls(mockFamilyManager);
        Mock.VerifyNoOtherCalls(mockMailManager);
        Mock.VerifyNoOtherCalls(mockTaskManager);
    }

    #endregion

    #region GetTemplate Tests

    [Test]
    public async Task GetTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var template = new CharacterTemplate
        {
            Race = Race.Nuian,
            Gender = Gender.Male,
            ModelId = 1,
            FactionId = FactionsEnum.NuiaAlliance
        };
        var templates = new Dictionary<byte, CharacterTemplate>
        {
            { (byte)(16 * (byte)Gender.Male + (byte)Race.Nuian), template }
        };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result = manager.GetTemplate(Race.Nuian, Gender.Male);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Race).IsEqualTo(Race.Nuian);
        await Assert.That(result.Gender).IsEqualTo(Gender.Male);
        await Assert.That(result.ModelId).IsEqualTo(1u);
    }

    [Test]
    public void GetTemplate_TemplateDoesNotExist_ThrowsKeyNotFoundException()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var templates = new Dictionary<byte, CharacterTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => manager.GetTemplate(Race.Nuian, Gender.Male));
    }

    [Test]
    public async Task GetTemplate_DifferentRaceGenderCombinations_ReturnsCorrectTemplates()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var nuianMale = new CharacterTemplate { Race = Race.Nuian, Gender = Gender.Male, ModelId = 1 };
        var nuianFemale = new CharacterTemplate { Race = Race.Nuian, Gender = Gender.Female, ModelId = 2 };
        var elfMale = new CharacterTemplate { Race = Race.Elf, Gender = Gender.Male, ModelId = 3 };
        var elfFemale = new CharacterTemplate { Race = Race.Elf, Gender = Gender.Female, ModelId = 4 };

        var templates = new Dictionary<byte, CharacterTemplate>
        {
            { (byte)(16 * (byte)Gender.Male + (byte)Race.Nuian), nuianMale },
            { (byte)(16 * (byte)Gender.Female + (byte)Race.Nuian), nuianFemale },
            { (byte)(16 * (byte)Gender.Male + (byte)Race.Elf), elfMale },
            { (byte)(16 * (byte)Gender.Female + (byte)Race.Elf), elfFemale }
        };
        SetPrivateField(manager, "_templates", templates);

        // Act & Assert
        await Assert.That(manager.GetTemplate(Race.Nuian, Gender.Male)).IsEqualTo(nuianMale);
        await Assert.That(manager.GetTemplate(Race.Nuian, Gender.Female)).IsEqualTo(nuianFemale);
        await Assert.That(manager.GetTemplate(Race.Elf, Gender.Male)).IsEqualTo(elfMale);
        await Assert.That(manager.GetTemplate(Race.Elf, Gender.Female)).IsEqualTo(elfFemale);
    }

    #endregion

    #region GetAppellationsTemplate Tests

    [Test]
    public async Task GetAppellationsTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var template = new AppellationTemplate { Id = 1, BuffId = 100 };
        var appellations = new Dictionary<uint, AppellationTemplate> { { 1, template } };
        SetPrivateField(manager, "_appellations", appellations);

        // Act
        var result = manager.GetAppellationsTemplate(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.BuffId).IsEqualTo(100u);
    }

    [Test]
    public async Task GetAppellationsTemplate_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var appellations = new Dictionary<uint, AppellationTemplate>();
        SetPrivateField(manager, "_appellations", appellations);

        // Act
        var result = manager.GetAppellationsTemplate(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetAppellationsTemplate_ZeroId_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var appellations = new Dictionary<uint, AppellationTemplate>();
        SetPrivateField(manager, "_appellations", appellations);

        // Act
        var result = manager.GetAppellationsTemplate(0);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetExpands Tests

    [Test]
    public async Task GetExpands_StepExists_ReturnsExpands()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var expands = new List<Expand>
        {
            new() { Step = 1, IsBank = false, Price = 100 },
            new() { Step = 1, IsBank = true, Price = 200 }
        };
        var expandsDict = new Dictionary<int, List<Expand>> { { 1, expands } };
        SetPrivateField(manager, "_expands", expandsDict);

        // Act
        var result = manager.GetExpands(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Count).IsEqualTo(2);
    }

    [Test]
    public void GetExpands_StepDoesNotExist_ThrowsKeyNotFoundException()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var expandsDict = new Dictionary<int, List<Expand>>();
        SetPrivateField(manager, "_expands", expandsDict);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => manager.GetExpands(999));
    }

    #endregion

    #region GetActability Tests

    [Test]
    public async Task GetActability_ActabilityExists_ReturnsActability()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var actability = new ActabilityTemplate { Id = 1, Name = "Alchemy", UnitAttributeId = 10 };
        var actabilities = new Dictionary<uint, ActabilityTemplate> { { 1, actability } };
        SetPrivateField(manager, "_actabilities", actabilities);

        // Act
        var result = manager.GetActability(1);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Id).IsEqualTo(1u);
        await Assert.That(result.Name).IsEqualTo("Alchemy");
    }

    [Test]
    public void GetActability_ActabilityDoesNotExist_ThrowsKeyNotFoundException()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var actabilities = new Dictionary<uint, ActabilityTemplate>();
        SetPrivateField(manager, "_actabilities", actabilities);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => manager.GetActability(999));
    }

    #endregion

    #region GetActabilityIdByCategoryId Tests

    [Test]
    public async Task GetActabilityIdByCategoryId_CategoryExists_ReturnsActabilityId()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var actabilityCategory = new ActabilityCategoriesTemplate { Id = 1, Name = "Category1", GroupId = 10 };
        var actability = new ActabilityTemplate { Id = 100, Name = "TestActability" };

        var categories = new Dictionary<uint, ActabilityCategoriesTemplate> { { 1, actabilityCategory } };
        var actabilities = new Dictionary<uint, ActabilityTemplate> { { 10, actability } };

        SetPrivateField(manager, "_actabilitiesCategories", categories);
        SetPrivateField(manager, "_actabilities", actabilities);

        // Act
        var result = manager.GetActabilityIdByCategoryId(1);

        // Assert
        await Assert.That(result).IsEqualTo(100u);
    }

    [Test]
    public async Task GetActabilityIdByCategoryId_CategoryDoesNotExist_ReturnsZero()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var categories = new Dictionary<uint, ActabilityCategoriesTemplate>();
        SetPrivateField(manager, "_actabilitiesCategories", categories);

        // Act
        var result = manager.GetActabilityIdByCategoryId(999);

        // Assert
        await Assert.That(result).IsEqualTo(0u);
    }

    [Test]
    public async Task GetActabilityIdByCategoryId_ActabilityNotFound_ReturnsZero()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var actabilityCategory = new ActabilityCategoriesTemplate { Id = 1, Name = "Category1", GroupId = 10 };
        var categories = new Dictionary<uint, ActabilityCategoriesTemplate> { { 1, actabilityCategory } };
        var actabilities = new Dictionary<uint, ActabilityTemplate>();

        SetPrivateField(manager, "_actabilitiesCategories", categories);
        SetPrivateField(manager, "_actabilities", actabilities);

        // Act
        var result = manager.GetActabilityIdByCategoryId(1);

        // Assert
        await Assert.That(result).IsEqualTo(0u);
    }

    #endregion

    #region GetExpertLimit Tests

    [Test]
    public async Task GetExpertLimit_StepExists_ReturnsExpertLimit()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limit = new ExpertLimit { Id = 1, UpLimit = 100, ExpertLimitCount = 2 };
        var limits = new Dictionary<int, ExpertLimit> { { 0, limit } };
        SetPrivateField(manager, "_expertLimits", limits);

        // Act
        var result = manager.GetExpertLimit(0);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.UpLimit).IsEqualTo(100);
        await Assert.That(result.ExpertLimitCount).IsEqualTo((byte)2);
    }

    [Test]
    public async Task GetExpertLimit_StepDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limits = new Dictionary<int, ExpertLimit>();
        SetPrivateField(manager, "_expertLimits", limits);

        // Act
        var result = manager.GetExpertLimit(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetExpandExpertLimit Tests

    [Test]
    public async Task GetExpandExpertLimit_StepExists_ReturnsExpandExpertLimit()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limit = new ExpandExpertLimit { Id = 1, ExpandCount = 1, LifePoint = 50 };
        var limits = new Dictionary<int, ExpandExpertLimit> { { 0, limit } };
        SetPrivateField(manager, "_expandExpertLimits", limits);

        // Act
        var result = manager.GetExpandExpertLimit(0);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.ExpandCount).IsEqualTo((byte)1);
        await Assert.That(result.LifePoint).IsEqualTo(50);
    }

    [Test]
    public async Task GetExpandExpertLimit_StepDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limits = new Dictionary<int, ExpandExpertLimit>();
        SetPrivateField(manager, "_expandExpertLimits", limits);

        // Act
        var result = manager.GetExpandExpertLimit(999);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region GetEffectiveAccessLevel Tests

    [Test]
    public async Task GetEffectiveAccessLevel_CharacterAccessLevelHigher_ReturnsCharacterAccessLevel()
    {
        // Arrange
        var mockAccountManager = Mock.Of<IAccountManager>();
        var manager = CreateCharacterManager(mockAccountManager: mockAccountManager);

        var character = new Character(new UnitCustomModelParams())
        {
            AccountId = 1,
            AccessLevel = 100
        };

        mockAccountManager
            .GetAccountDetails(1)
            .Returns(new AccountDetails { AccountId = 1, AccessLevel = 50 });

        // Act
        var result = manager.GetEffectiveAccessLevel(character);

        // Assert
        await Assert.That(result).IsEqualTo(100);
    }

    [Test]
    public async Task GetEffectiveAccessLevel_AccountAccessLevelHigher_ReturnsAccountAccessLevel()
    {
        // Arrange
        var mockAccountManager = Mock.Of<IAccountManager>();
        var manager = CreateCharacterManager(mockAccountManager: mockAccountManager);

        var character = new Character(new UnitCustomModelParams())
        {
            AccountId = 1,
            AccessLevel = 50
        };

        mockAccountManager
            .GetAccountDetails(1)
            .Returns(new AccountDetails { AccountId = 1, AccessLevel = 100 });

        // Act
        var result = manager.GetEffectiveAccessLevel(character);

        // Assert
        await Assert.That(result).IsEqualTo(100);
    }

    [Test]
    public async Task GetEffectiveAccessLevel_EqualAccessLevels_ReturnsAccessLevel()
    {
        // Arrange
        var mockAccountManager = Mock.Of<IAccountManager>();
        var manager = CreateCharacterManager(mockAccountManager: mockAccountManager);

        var character = new Character(new UnitCustomModelParams())
        {
            AccountId = 1,
            AccessLevel = 50
        };

        mockAccountManager
            .GetAccountDetails(1)
            .Returns(new AccountDetails { AccountId = 1, AccessLevel = 50 });

        // Act
        var result = manager.GetEffectiveAccessLevel(character);

        // Assert
        await Assert.That(result).IsEqualTo(50);
    }

    #endregion

    #region PlayerRoll Tests

    [Test]
    public void PlayerRoll_ValidMax_SendsRollMessage()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var character = new Character(new UnitCustomModelParams())
        {
            Name = "TestPlayer"
        };

        // Act & Assert - No exception should be thrown
        manager.PlayerRoll(character, 100);
    }

    [Test]
    public void PlayerRoll_MaxValueOne_SendsRollOne()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var character = new Character(new UnitCustomModelParams())
        {
            Name = "TestPlayer"
        };

        // Act & Assert - No exception should be thrown
        manager.PlayerRoll(character, 1);
    }

    #endregion

    #region Edge Cases Tests

    [Test]
    public void GetTemplate_MaxByteValues_ThrowsKeyNotFoundException()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var templates = new Dictionary<byte, CharacterTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => manager.GetTemplate((Race)255, (Gender)15));
    }

    [Test]
    public async Task GetActabilityIdByCategoryId_ZeroCategoryId_ReturnsZero()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var categories = new Dictionary<uint, ActabilityCategoriesTemplate>();
        SetPrivateField(manager, "_actabilitiesCategories", categories);

        // Act
        var result = manager.GetActabilityIdByCategoryId(0);

        // Assert
        await Assert.That(result).IsEqualTo(0u);
    }

    [Test]
    public async Task GetExpertLimit_NegativeStep_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limits = new Dictionary<int, ExpertLimit>();
        SetPrivateField(manager, "_expertLimits", limits);

        // Act
        var result = manager.GetExpertLimit(-1);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetLanguageExpertLimit_StepExists_ReturnsLimit()
    {
        var manager = CreateCharacterManager();
        var limit = new ExpertLimit { Id = 32, UpLimit = 20000, UseLanguageType = true };
        SetPrivateField(manager, "_languageExpertLimits", new Dictionary<int, ExpertLimit> { { 0, limit } });

        var result = manager.GetLanguageExpertLimit(0);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.UpLimit).IsEqualTo(20000);
    }

    [Test]
    public async Task GetPointCapLimit_LanguageUsesLanguageCap()
    {
        var manager = CreateCharacterManager();
        var production = new ExpertLimit { Id = 1, UpLimit = 10000 };
        var language = new ExpertLimit { Id = 32, UpLimit = 20000, UseLanguageType = true };
        SetPrivateField(manager, "_expertLimits", new Dictionary<int, ExpertLimit> { { 0, production } });
        SetPrivateField(manager, "_languageExpertLimits", new Dictionary<int, ExpertLimit> { { 0, language } });

        var fishing = manager.GetPointCapLimit((uint)ActabilityType.Fishing, 0);
        var nuian = manager.GetPointCapLimit((uint)ActabilityType.NuianLanguage, 0);

        await Assert.That(fishing.UpLimit).IsEqualTo(10000);
        await Assert.That(nuian.UpLimit).IsEqualTo(20000);
    }

    [Test]
    public async Task GetExpandExpertLimit_NegativeStep_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limits = new Dictionary<int, ExpandExpertLimit>();
        SetPrivateField(manager, "_expandExpertLimits", limits);

        // Act
        var result = manager.GetExpandExpertLimit(-1);

        // Assert
        await Assert.That(result).IsNull();
    }

    [Test]
    public void GetExpands_ZeroStep_ThrowsKeyNotFoundException()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var expandsDict = new Dictionary<int, List<Expand>>();
        SetPrivateField(manager, "_expands", expandsDict);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => manager.GetExpands(0));
    }

    [Test]
    public async Task GetAppellationsTemplate_MaxUIntId_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var appellations = new Dictionary<uint, AppellationTemplate>();
        SetPrivateField(manager, "_appellations", appellations);

        // Act
        var result = manager.GetAppellationsTemplate(uint.MaxValue);

        // Assert
        await Assert.That(result).IsNull();
    }

    #endregion

    #region Multiple Operations Tests

    [Test]
    public async Task MultipleGetTemplateCalls_ReturnConsistentResults()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var template = new CharacterTemplate
        {
            Race = Race.Nuian,
            Gender = Gender.Male,
            ModelId = 1
        };
        var templates = new Dictionary<byte, CharacterTemplate>
        {
            { (byte)(16 * (byte)Gender.Male + (byte)Race.Nuian), template }
        };
        SetPrivateField(manager, "_templates", templates);

        // Act
        var result1 = manager.GetTemplate(Race.Nuian, Gender.Male);
        var result2 = manager.GetTemplate(Race.Nuian, Gender.Male);
        var result3 = manager.GetTemplate(Race.Nuian, Gender.Male);

        // Assert
        await Assert.That(result1).IsNotNull();
        await Assert.That(result2).IsNotNull();
        await Assert.That(result3).IsNotNull();
        await Assert.That(result2).IsSameReferenceAs(result1);
        await Assert.That(result3).IsSameReferenceAs(result2);
    }

    [Test]
    public async Task GetTemplate_AfterModifyingDictionary_ReturnsUpdatedTemplate()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var template1 = new CharacterTemplate
        {
            Race = Race.Nuian,
            Gender = Gender.Male,
            ModelId = 1
        };
        var templates = new Dictionary<byte, CharacterTemplate>
        {
            { (byte)(16 * (byte)Gender.Male + (byte)Race.Nuian), template1 }
        };
        SetPrivateField(manager, "_templates", templates);

        // Act - Get original template
        var result1 = manager.GetTemplate(Race.Nuian, Gender.Male);

        // Modify dictionary
        var template2 = new CharacterTemplate
        {
            Race = Race.Nuian,
            Gender = Gender.Male,
            ModelId = 999
        };
        templates[(byte)(16 * (byte)Gender.Male + (byte)Race.Nuian)] = template2;

        // Get template again
        var result2 = manager.GetTemplate(Race.Nuian, Gender.Male);

        // Assert
        await Assert.That(result1.ModelId).IsEqualTo(1u);
        await Assert.That(result2.ModelId).IsEqualTo(999u);
    }

    #endregion

    #region Helper Methods

    private static CharacterManager CreateCharacterManager(
        Mock<IWorldManager> mockWorldManager = null,
        Mock<IAccountManager> mockAccountManager = null,
        Mock<INameManager> mockNameManager = null,
        Mock<ICharacterIdManager> mockCharacterIdManager = null,
        Mock<IFactionManager> mockFactionManager = null,
        Mock<ISkillManager> mockSkillManager = null,
        Mock<IItemManager> mockItemManager = null,
        Mock<IHousingManager> mockHousingManager = null,
        Mock<IFamilyManager> mockFamilyManager = null,
        Mock<IMailManager> mockMailManager = null,
        Mock<ITaskManager> mockTaskManager = null)
    {
        return new CharacterManager(
            (mockWorldManager ?? Mock.Of<IWorldManager>()).Object,
            (mockAccountManager ?? Mock.Of<IAccountManager>()).Object,
            (mockNameManager ?? Mock.Of<INameManager>()).Object,
            (mockCharacterIdManager ?? Mock.Of<ICharacterIdManager>()).Object,
            (mockFactionManager ?? Mock.Of<IFactionManager>()).Object,
            (mockSkillManager ?? Mock.Of<ISkillManager>()).Object,
            (mockItemManager ?? Mock.Of<IItemManager>()).Object,
            (mockHousingManager ?? Mock.Of<IHousingManager>()).Object,
            (mockFamilyManager ?? Mock.Of<IFamilyManager>()).Object,
            (mockMailManager ?? Mock.Of<IMailManager>()).Object,
            (mockTaskManager ?? Mock.Of<ITaskManager>()).Object);
    }

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(obj, value);
    }

    #endregion
}
