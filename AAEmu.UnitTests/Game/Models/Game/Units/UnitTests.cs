using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.NPChar;
using AAEmu.Game.Models.Game.Units;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Game.Units;

public class UnitTests
{
    #region UnitAttribute Tests

    [Fact]
    public void NoDuplicateAttributes()
    {
        //This tests to make sure no Attribute is attached to more than one property
        var unit = new Unit();

        foreach (var attr in Enum.GetValues<UnitAttribute>())
        {
            var props = unit.GetType().GetProperties()
            .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                .Any(a => a.Attributes.Contains(attr)));

            Assert.True(props.Count() <= 1, $"{attr} is bound to multiple unit properties.");
        }

        unit = new Character(new UnitCustomModelParams());
        foreach (var attr in Enum.GetValues<UnitAttribute>())
        {
            var props = unit.GetType().GetProperties()
            .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                .Any(a => a.Attributes.Contains(attr)));

            Assert.True(props.Count() <= 1, $"{attr} is bound to multiple unit properties.");
        }

        unit = new Npc();
        foreach (var attr in Enum.GetValues<UnitAttribute>())
        {
            var props = unit.GetType().GetProperties()
            .Where(o => (o.GetCustomAttributes(typeof(UnitAttributeAttribute), true) as IEnumerable<UnitAttributeAttribute>)
                .Any(a => a.Attributes.Contains(attr)));

            Assert.True(props.Count() <= 1, $"{attr} is bound to multiple unit properties.");
        }
    }

    #endregion

    #region Unit Model Tests

    [Fact]
    public void Unit_DefaultConstructor_CreatesInstance()
    {
        // Act
        var unit = new Unit();

        // Assert
        Assert.NotNull(unit);
    }

    [Fact]
    public void Unit_ObjId_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.ObjId = 12345u;

        // Assert
        Assert.Equal(12345u, unit.ObjId);
    }

    [Fact]
    public void Unit_TemplateId_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.TemplateId = 100u;

        // Assert
        Assert.Equal(100u, unit.TemplateId);
    }

    [Fact]
    public void Unit_Name_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Name = "TestUnit";

        // Assert
        Assert.Equal("TestUnit", unit.Name);
    }

    [Fact]
    public void Unit_Level_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Level = 50;

        // Assert
        Assert.Equal(50, unit.Level);
    }

    [Fact]
    public void Unit_Hp_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Hp = 1000;
        unit.MaxHp = 2000;

        // Assert
        Assert.Equal(1000, unit.Hp);
        Assert.Equal(2000, unit.MaxHp);
    }

    [Fact]
    public void Unit_Mp_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Mp = 500;
        unit.MaxMp = 1000;

        // Assert
        Assert.Equal(500, unit.Mp);
        Assert.Equal(1000, unit.MaxMp);
    }

    #endregion

    #region Character Specific Tests

    [Fact]
    public void Character_DefaultConstructor_CreatesInstance()
    {
        // Act
        var character = new Character(new UnitCustomModelParams());

        // Assert
        Assert.NotNull(character);
    }

    [Fact]
    public void Character_AccountId_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var character = new Character(new UnitCustomModelParams());

        // Act
        character.AccountId = 45678u;

        // Assert
        Assert.Equal(45678u, character.AccountId);
    }

    [Fact]
    public void Character_Level_SetAndGet_ReturnsCorrectValue()
    {
        // Arrange
        var character = new Character(new UnitCustomModelParams());

        // Act
        character.Level = 25;

        // Assert
        Assert.Equal(25, character.Level);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void Character_ObjId_ExtremeValues(uint value)
    {
        // Arrange
        var character = new Character(new UnitCustomModelParams());

        // Act
        character.ObjId = value;

        // Assert
        Assert.Equal(value, character.ObjId);
    }

    #endregion

    #region Npc Specific Tests

    [Fact]
    public void Npc_DefaultConstructor_CreatesInstance()
    {
        // Act
        var npc = new Npc();

        // Assert
        Assert.NotNull(npc);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData(0u)]
    [InlineData(uint.MaxValue)]
    public void Unit_ObjId_ExtremeValues(uint value)
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.ObjId = value;

        // Assert
        Assert.Equal(value, unit.ObjId);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData((byte)100)]
    public void Unit_Level_ValidValues(byte level)
    {
        // Arrange
        var unit = new Unit();

        // Act
        unit.Level = level;

        // Assert
        Assert.Equal(level, unit.Level);
    }

    [Fact]
    public void Unit_DefaultValues_AreZeroOrNull()
    {
        // Arrange
        var unit = new Unit();

        // Assert
        Assert.Equal(0u, unit.ObjId);
        Assert.Equal(0u, unit.TemplateId);
        Assert.Null(unit.Name);
        Assert.Equal(0, unit.Level);
        Assert.Equal(0, unit.Hp);
        Assert.Equal(0, unit.MaxHp);
        Assert.Equal(0, unit.Mp);
        Assert.Equal(0, unit.MaxMp);
    }

    #endregion
}
