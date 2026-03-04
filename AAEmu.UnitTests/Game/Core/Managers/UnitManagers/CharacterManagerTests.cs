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

using Moq;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers.UnitManagers;

public class CharacterManagerTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_DoesNotCallDependencies()
    {
        // Arrange
        var mockWorldManager = new Mock<IWorldManager>();
        var mockAccountManager = new Mock<IAccountManager>();
        var mockNameManager = new Mock<INameManager>();
        var mockCharacterIdManager = new Mock<ICharacterIdManager>();
        var mockFactionManager = new Mock<IFactionManager>();
        var mockSkillManager = new Mock<ISkillManager>();
        var mockItemManager = new Mock<IItemManager>();
        var mockHousingManager = new Mock<IHousingManager>();
        var mockFamilyManager = new Mock<IFamilyManager>();
        var mockMailManager = new Mock<IMailManager>();
        var mockTaskManager = new Mock<ITaskManager>();

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
        Assert.NotNull(manager);
        mockWorldManager.VerifyNoOtherCalls();
        mockAccountManager.VerifyNoOtherCalls();
        mockNameManager.VerifyNoOtherCalls();
        mockCharacterIdManager.VerifyNoOtherCalls();
        mockFactionManager.VerifyNoOtherCalls();
        mockSkillManager.VerifyNoOtherCalls();
        mockItemManager.VerifyNoOtherCalls();
        mockHousingManager.VerifyNoOtherCalls();
        mockFamilyManager.VerifyNoOtherCalls();
        mockMailManager.VerifyNoOtherCalls();
        mockTaskManager.VerifyNoOtherCalls();
    }

    #endregion

    #region GetTemplate Tests

    [Fact]
    public void GetTemplate_TemplateExists_ReturnsTemplate()
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
        Assert.NotNull(result);
        Assert.Equal(Race.Nuian, result.Race);
        Assert.Equal(Gender.Male, result.Gender);
        Assert.Equal(1u, result.ModelId);
    }

    [Fact]
    public void GetTemplate_TemplateDoesNotExist_ThrowsKeyNotFoundException()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var templates = new Dictionary<byte, CharacterTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => manager.GetTemplate(Race.Nuian, Gender.Male));
    }

    [Fact]
    public void GetTemplate_DifferentRaceGenderCombinations_ReturnsCorrectTemplates()
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
        Assert.Equal(nuianMale, manager.GetTemplate(Race.Nuian, Gender.Male));
        Assert.Equal(nuianFemale, manager.GetTemplate(Race.Nuian, Gender.Female));
        Assert.Equal(elfMale, manager.GetTemplate(Race.Elf, Gender.Male));
        Assert.Equal(elfFemale, manager.GetTemplate(Race.Elf, Gender.Female));
    }

    #endregion

    #region GetAppellationsTemplate Tests

    [Fact]
    public void GetAppellationsTemplate_TemplateExists_ReturnsTemplate()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var template = new AppellationTemplate { Id = 1, BuffId = 100 };
        var appellations = new Dictionary<uint, AppellationTemplate> { { 1, template } };
        SetPrivateField(manager, "_appellations", appellations);

        // Act
        var result = manager.GetAppellationsTemplate(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal(100u, result.BuffId);
    }

    [Fact]
    public void GetAppellationsTemplate_TemplateDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var appellations = new Dictionary<uint, AppellationTemplate>();
        SetPrivateField(manager, "_appellations", appellations);

        // Act
        var result = manager.GetAppellationsTemplate(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetAppellationsTemplate_ZeroId_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var appellations = new Dictionary<uint, AppellationTemplate>();
        SetPrivateField(manager, "_appellations", appellations);

        // Act
        var result = manager.GetAppellationsTemplate(0);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetExpands Tests

    [Fact]
    public void GetExpands_StepExists_ReturnsExpands()
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
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
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

    [Fact]
    public void GetActability_ActabilityExists_ReturnsActability()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var actability = new ActabilityTemplate { Id = 1, Name = "Alchemy", UnitAttributeId = 10 };
        var actabilities = new Dictionary<uint, ActabilityTemplate> { { 1, actability } };
        SetPrivateField(manager, "_actabilities", actabilities);

        // Act
        var result = manager.GetActability(1);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1u, result.Id);
        Assert.Equal("Alchemy", result.Name);
    }

    [Fact]
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

    [Fact]
    public void GetActabilityIdByCategoryId_CategoryExists_ReturnsActabilityId()
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
        Assert.Equal(100u, result);
    }

    [Fact]
    public void GetActabilityIdByCategoryId_CategoryDoesNotExist_ReturnsZero()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var categories = new Dictionary<uint, ActabilityCategoriesTemplate>();
        SetPrivateField(manager, "_actabilitiesCategories", categories);

        // Act
        var result = manager.GetActabilityIdByCategoryId(999);

        // Assert
        Assert.Equal(0u, result);
    }

    [Fact]
    public void GetActabilityIdByCategoryId_ActabilityNotFound_ReturnsZero()
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
        Assert.Equal(0u, result);
    }

    #endregion

    #region GetExpertLimit Tests

    [Fact]
    public void GetExpertLimit_StepExists_ReturnsExpertLimit()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limit = new ExpertLimit { Id = 1, UpLimit = 100, ExpertLimitCount = 2 };
        var limits = new Dictionary<int, ExpertLimit> { { 0, limit } };
        SetPrivateField(manager, "_expertLimits", limits);

        // Act
        var result = manager.GetExpertLimit(0);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(100, result.UpLimit);
        Assert.Equal(2, result.ExpertLimitCount);
    }

    [Fact]
    public void GetExpertLimit_StepDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limits = new Dictionary<int, ExpertLimit>();
        SetPrivateField(manager, "_expertLimits", limits);

        // Act
        var result = manager.GetExpertLimit(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetExpandExpertLimit Tests

    [Fact]
    public void GetExpandExpertLimit_StepExists_ReturnsExpandExpertLimit()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limit = new ExpandExpertLimit { Id = 1, ExpandCount = 1, LifePoint = 50 };
        var limits = new Dictionary<int, ExpandExpertLimit> { { 0, limit } };
        SetPrivateField(manager, "_expandExpertLimits", limits);

        // Act
        var result = manager.GetExpandExpertLimit(0);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.ExpandCount);
        Assert.Equal(50, result.LifePoint);
    }

    [Fact]
    public void GetExpandExpertLimit_StepDoesNotExist_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limits = new Dictionary<int, ExpandExpertLimit>();
        SetPrivateField(manager, "_expandExpertLimits", limits);

        // Act
        var result = manager.GetExpandExpertLimit(999);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region GetEffectiveAccessLevel Tests

    [Fact]
    public void GetEffectiveAccessLevel_CharacterAccessLevelHigher_ReturnsCharacterAccessLevel()
    {
        // Arrange
        var mockAccountManager = new Mock<IAccountManager>();
        var manager = CreateCharacterManager(mockAccountManager: mockAccountManager);

        var character = new Character(new UnitCustomModelParams())
        {
            AccountId = 1,
            AccessLevel = 100
        };

        mockAccountManager
            .Setup(x => x.GetAccountDetails(1))
            .Returns(new AccountDetails { AccountId = 1, AccessLevel = 50 });

        // Act
        var result = manager.GetEffectiveAccessLevel(character);

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void GetEffectiveAccessLevel_AccountAccessLevelHigher_ReturnsAccountAccessLevel()
    {
        // Arrange
        var mockAccountManager = new Mock<IAccountManager>();
        var manager = CreateCharacterManager(mockAccountManager: mockAccountManager);

        var character = new Character(new UnitCustomModelParams())
        {
            AccountId = 1,
            AccessLevel = 50
        };

        mockAccountManager
            .Setup(x => x.GetAccountDetails(1))
            .Returns(new AccountDetails { AccountId = 1, AccessLevel = 100 });

        // Act
        var result = manager.GetEffectiveAccessLevel(character);

        // Assert
        Assert.Equal(100, result);
    }

    [Fact]
    public void GetEffectiveAccessLevel_EqualAccessLevels_ReturnsAccessLevel()
    {
        // Arrange
        var mockAccountManager = new Mock<IAccountManager>();
        var manager = CreateCharacterManager(mockAccountManager: mockAccountManager);

        var character = new Character(new UnitCustomModelParams())
        {
            AccountId = 1,
            AccessLevel = 50
        };

        mockAccountManager
            .Setup(x => x.GetAccountDetails(1))
            .Returns(new AccountDetails { AccountId = 1, AccessLevel = 50 });

        // Act
        var result = manager.GetEffectiveAccessLevel(character);

        // Assert
        Assert.Equal(50, result);
    }

    #endregion

    #region PlayerRoll Tests

    [Fact]
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

    [Fact]
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

    [Fact]
    public void GetTemplate_MaxByteValues_ThrowsKeyNotFoundException()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var templates = new Dictionary<byte, CharacterTemplate>();
        SetPrivateField(manager, "_templates", templates);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => manager.GetTemplate((Race)255, (Gender)15));
    }

    [Fact]
    public void GetActabilityIdByCategoryId_ZeroCategoryId_ReturnsZero()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var categories = new Dictionary<uint, ActabilityCategoriesTemplate>();
        SetPrivateField(manager, "_actabilitiesCategories", categories);

        // Act
        var result = manager.GetActabilityIdByCategoryId(0);

        // Assert
        Assert.Equal(0u, result);
    }

    [Fact]
    public void GetExpertLimit_NegativeStep_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limits = new Dictionary<int, ExpertLimit>();
        SetPrivateField(manager, "_expertLimits", limits);

        // Act
        var result = manager.GetExpertLimit(-1);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetExpandExpertLimit_NegativeStep_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var limits = new Dictionary<int, ExpandExpertLimit>();
        SetPrivateField(manager, "_expandExpertLimits", limits);

        // Act
        var result = manager.GetExpandExpertLimit(-1);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetExpands_ZeroStep_ThrowsKeyNotFoundException()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var expandsDict = new Dictionary<int, List<Expand>>();
        SetPrivateField(manager, "_expands", expandsDict);

        // Act & Assert
        Assert.Throws<KeyNotFoundException>(() => manager.GetExpands(0));
    }

    [Fact]
    public void GetAppellationsTemplate_MaxUIntId_ReturnsNull()
    {
        // Arrange
        var manager = CreateCharacterManager();
        var appellations = new Dictionary<uint, AppellationTemplate>();
        SetPrivateField(manager, "_appellations", appellations);

        // Act
        var result = manager.GetAppellationsTemplate(uint.MaxValue);

        // Assert
        Assert.Null(result);
    }

    #endregion

    #region Multiple Operations Tests

    [Fact]
    public void MultipleGetTemplateCalls_ReturnConsistentResults()
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
        Assert.NotNull(result1);
        Assert.NotNull(result2);
        Assert.NotNull(result3);
        Assert.Same(result1, result2);
        Assert.Same(result2, result3);
    }

    [Fact]
    public void GetTemplate_AfterModifyingDictionary_ReturnsUpdatedTemplate()
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
        Assert.Equal(1u, result1.ModelId);
        Assert.Equal(999u, result2.ModelId);
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
            (mockWorldManager ?? new Mock<IWorldManager>()).Object,
            (mockAccountManager ?? new Mock<IAccountManager>()).Object,
            (mockNameManager ?? new Mock<INameManager>()).Object,
            (mockCharacterIdManager ?? new Mock<ICharacterIdManager>()).Object,
            (mockFactionManager ?? new Mock<IFactionManager>()).Object,
            (mockSkillManager ?? new Mock<ISkillManager>()).Object,
            (mockItemManager ?? new Mock<IItemManager>()).Object,
            (mockHousingManager ?? new Mock<IHousingManager>()).Object,
            (mockFamilyManager ?? new Mock<IFamilyManager>()).Object,
            (mockMailManager ?? new Mock<IMailManager>()).Object,
            (mockTaskManager ?? new Mock<ITaskManager>()).Object);
    }

    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(obj, value);
    }

    #endregion
}
