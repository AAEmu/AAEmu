using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class UnitTests
{
    #region UnitAttribute Tests

    [Test]
    public async Task NoDuplicateAttributes()
    {
        //This tests to make sure no Attribute is attached to more than one property
        var unit = new Unit();

        foreach (var attr in Enum.GetValues<UnitAttribute>())
        {
            var props = unit.GetType().GetProperties()
            .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                .Any(a => a.Attributes.Contains(attr)));

            await Assert.That(props.Count() <= 1).IsTrue().Because($"{attr} is bound to multiple unit properties.");
        }

        unit = new Character(new UnitCustomModelParams());
        foreach (var attr in Enum.GetValues<UnitAttribute>())
        {
            var props = unit.GetType().GetProperties()
            .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                .Any(a => a.Attributes.Contains(attr)));

            await Assert.That(props.Count() <= 1).IsTrue().Because($"{attr} is bound to multiple unit properties.");
        }

        unit = new Npc();
        foreach (var attr in Enum.GetValues<UnitAttribute>())
        {
            var props = unit.GetType().GetProperties()
            .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                .Any(a => a.Attributes.Contains(attr)));

            await Assert.That(props.Count() <= 1).IsTrue().Because($"{attr} is bound to multiple unit properties.");
        }
    }

    #endregion

    #region Unit Model Tests

    [Test]
    public async Task Unit_DefaultConstructor_CreatesInstance()
    {
        // Act
        var unit = new Unit();

        // Assert
        await Assert.That(unit).IsNotNull();
    }

    [Test]
    public async Task Unit_ObjId_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.ObjId = 12345u;

        // Assert
        await Assert.That(unit.ObjId).IsEqualTo(12345u);
    }

    [Test]
    public async Task Unit_TemplateId_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.TemplateId = 100u;

        // Assert
        await Assert.That(unit.TemplateId).IsEqualTo(100u);
    }

    [Test]
    public async Task Unit_Name_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Name = "TestUnit";

        // Assert
        await Assert.That(unit.Name).IsEqualTo("TestUnit");
    }

    [Test]
    public async Task Unit_Level_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Level = 50;

        // Assert
        await Assert.That(unit.Level).IsEqualTo((byte)50);
    }

    [Test]
    public async Task Unit_Hp_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Hp = 1000;
        unit.MaxHp = 2000;

        // Assert
        await Assert.That(unit.Hp).IsEqualTo(1000);
        await Assert.That(unit.MaxHp).IsEqualTo(2000);
    }

    [Test]
    public async Task Unit_Mp_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Mp = 500;
        unit.MaxMp = 1000;

        // Assert
        await Assert.That(unit.Mp).IsEqualTo(500);
        await Assert.That(unit.MaxMp).IsEqualTo(1000);
    }

    #endregion

    #region Character Specific Tests

    [Test]
    public async Task Character_DefaultConstructor_CreatesInstance()
    {
        // Act
        var character = new Character(new UnitCustomModelParams());

        // Assert
        await Assert.That(character).IsNotNull();
    }

    [Test]
    public async Task Character_AccountId_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var character = new Character(new UnitCustomModelParams());

        // Act
        character.AccountId = 45678u;

        // Assert
        await Assert.That(character.AccountId).IsEqualTo(45678u);
    }

    [Test]
    public async Task Character_Level_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var character = new Character(new UnitCustomModelParams());

        // Act
        character.Level = 25;

        // Assert
        await Assert.That(character.Level).IsEqualTo((byte)25);
    }

    [Test]
    [Arguments(0u)]
    [Arguments(uint.MaxValue)]
    public async Task Character_ObjId_ExtremeValues(uint value)
    {
        // Arrange
        var character = new Character(new UnitCustomModelParams());

        // Act
        character.ObjId = value;

        // Assert
        await Assert.That(character.ObjId).IsEqualTo(value);
    }

    #endregion

    #region Npc Specific Tests

    [Test]
    public async Task Npc_DefaultConstructor_CreatesInstance()
    {
        // Act
        var npc = new Npc();

        // Assert
        await Assert.That(npc).IsNotNull();
    }

    #endregion

    #region Edge Cases

    [Test]
    [Arguments(0u)]
    [Arguments(uint.MaxValue)]
    public async Task Unit_ObjId_ExtremeValues(uint value)
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.ObjId = value;

        // Assert
        await Assert.That(unit.ObjId).IsEqualTo(value);
    }

    [Test]
    [Arguments((byte)0)]
    [Arguments((byte)1)]
    [Arguments((byte)100)]
    public async Task Unit_Level_ValidValues(byte level)
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Level = level;

        // Assert
        await Assert.That(unit.Level).IsEqualTo(level);
    }

    [Test]
    public async Task Unit_DefaultValues_AreZeroOrNull()
    {
        // Arrange
        var unit = new Unit();

        // Assert
        await Assert.That(unit.ObjId).IsEqualTo(0u);
        await Assert.That(unit.TemplateId).IsEqualTo(0u);
        await Assert.That(unit.Name).IsNull();
        await Assert.That(unit.Level).IsEqualTo((byte)0);
        await Assert.That(unit.Hp).IsEqualTo(0);
        await Assert.That(unit.MaxHp).IsEqualTo(0);
        await Assert.That(unit.Mp).IsEqualTo(0);
        await Assert.That(unit.MaxMp).IsEqualTo(0);
    }

    #endregion
}
