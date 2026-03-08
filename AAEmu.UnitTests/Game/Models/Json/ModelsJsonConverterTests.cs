using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils.Converters;
using Newtonsoft.Json;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Json;

public class ModelsJsonConverterTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_Default_CreatesInstanceWithConverters()
    {
        // Act
        var converter = new JsonModelsConverter();

        // Assert
        Assert.NotNull(converter);
    }

    #endregion

    #region CanConvert Tests

    [Theory]
    [InlineData(typeof(JsonPosition), true)]
    [InlineData(typeof(JsonQuestSphere), true)]
    [InlineData(typeof(JsonDoodadSpawns), true)]
    [InlineData(typeof(JsonNpcSpawns), true)]
    [InlineData(typeof(string), false)]
    [InlineData(typeof(int), false)]
    [InlineData(typeof(object), false)]
    public void CanConvert_ReturnsCorrectValue(Type objectType, bool expected)
    {
        // Arrange
        var converter = new JsonModelsConverter();

        // Act
        var result = converter.CanConvert(objectType);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region AddConverter Tests

    [Fact]
    public void AddConverter_WithValidTypes_AddsConverter()
    {
        // Arrange
        var converter = new JsonModelsConverter();

        // This tests the internal behavior - we verify by checking CanConvert works
        // Act & Assert - should not throw
        var canConvertPosition = converter.CanConvert(typeof(JsonPosition));
        Assert.True(canConvertPosition);
    }

    #endregion

    #region WriteJson Tests

    [Fact]
    public void WriteJson_WithJsonPosition_WritesCorrectJson()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var position = new JsonPosition
        {
            X = 100.5f,
            Y = 200.5f,
            Z = 300.5f,
            Yaw = 0,
            Pitch = 0,
            Roll = 0
        };

        // Act
        var json = JsonConvert.SerializeObject(position, converter);

        // Assert
        Assert.Contains("100.5", json);
        Assert.Contains("200.5", json);
        Assert.Contains("300.5", json);
    }

    [Fact]
    public void WriteJson_WithRotationValues_WritesRotation()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var position = new JsonPosition
        {
            X = 1f,
            Y = 2f,
            Z = 3f,
            Yaw = 45,
            Pitch = 30,
            Roll = 15
        };

        // Act
        var json = JsonConvert.SerializeObject(position, converter);

        // Assert
        Assert.Contains("45", json);
        Assert.Contains("30", json);
        Assert.Contains("15", json);
    }

    [Fact]
    public void WriteJson_WithZeroRotation_OmitsRotationFields()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var position = new JsonPosition
        {
            X = 1f,
            Y = 1f,
            Z = 1f,
            Yaw = 0,
            Pitch = 0,
            Roll = 0
        };

        // Act
        var json = JsonConvert.SerializeObject(position, converter);

        // Assert - should not contain yaw/pitch/roll when zero
        Assert.DoesNotContain("Yaw", json);
        Assert.DoesNotContain("Pitch", json);
        Assert.DoesNotContain("Roll", json);
    }

    #endregion

    #region Array Serialization Tests

    [Fact]
    public void SerializeObject_WithArrayOfPositions_WritesCorrectJson()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var positions = new[]
        {
            new JsonPosition { X = 1, Y = 2, Z = 3 },
            new JsonPosition { X = 4, Y = 5, Z = 6 }
        };

        // Act
        var json = JsonConvert.SerializeObject(positions, converter);

        // Assert
        Assert.Contains("1", json);
        Assert.Contains("2", json);
        Assert.Contains("3", json);
        Assert.Contains("4", json);
        Assert.Contains("5", json);
        Assert.Contains("6", json);
    }

    #endregion

    #region ReadJson Tests

    [Fact]
    public void ReadJson_WithValidJsonPosition_ReturnsPosition()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var json = "{\"X\":10.5,\"Y\":20.5,\"Z\":30.5}";

        // Act
        var result = JsonConvert.DeserializeObject<JsonPosition>(json, converter);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(10.5f, result.X);
        Assert.Equal(20.5f, result.Y);
        Assert.Equal(30.5f, result.Z);
    }

    [Fact]
    public void ReadJson_WithRotation_ReturnsPositionWithRotation()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var json = "{\"X\":1,\"Y\":2,\"Z\":3,\"Yaw\":45,\"Pitch\":30,\"Roll\":15}";

        // Act
        var result = JsonConvert.DeserializeObject<JsonPosition>(json, converter);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(45, result.Yaw);
        Assert.Equal(30, result.Pitch);
        Assert.Equal(15, result.Roll);
    }

    #endregion

    #region Edge Cases

    [Theory]
    [InlineData("{\"X\":0,\"Y\":0,\"Z\":0}", 0, 0, 0)]
    [InlineData("{\"X\":-100.5,\"Y\":-200.5,\"Z\":-300.5}", -100.5f, -200.5f, -300.5f)]
    [InlineData("{\"X\":1.23456789,\"Y\":2.34567890,\"Z\":3.45678901}", 1.23456789f, 2.34567890f, 3.45678901f)]
    public void ReadJson_WithVariousValues_ReturnsCorrectPosition(string json, float expectedX, float expectedY, float expectedZ)
    {
        // Arrange
        var converter = new JsonModelsConverter();

        // Act
        var result = JsonConvert.DeserializeObject<JsonPosition>(json, converter);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
        Assert.Equal(expectedZ, result.Z);
    }

    #endregion

    #region JsonNpcSpawns Tests

    [Fact]
    public void SerializeNpcSpawns_WithAllFields_WritesCorrectJson()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var npcSpawns = new JsonNpcSpawns
        {
            Id = 1,
            UnitId = 100,
            Title = "Test NPC",
            FollowPath = "/path/to/follow",
            Position = new JsonPosition
            {
                X = 1000f,
                Y = 2000f,
                Z = 3000f,
                Yaw = 90,
                Pitch = 0,
                Roll = 0
            },
            Scale = 1.5f
        };

        // Act
        var json = JsonConvert.SerializeObject(npcSpawns, converter);

        // Assert
        Assert.Contains("1", json);
        Assert.Contains("100", json);
        Assert.Contains("Test NPC", json);
        Assert.Contains("1.5", json);
    }

    [Fact]
    public void SerializeNpcSpawns_WithZeroPosition_OmitsRotation()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var npcSpawns = new JsonNpcSpawns
        {
            Id = 1,
            UnitId = 1,
            Title = "test",
            FollowPath = "test",
            Position = new JsonPosition
            {
                X = 1,
                Y = 1,
                Z = 1,
                Yaw = 0,
                Pitch = 0,
                Roll = 0
            },
            Scale = 1f
        };

        // Act
        var json = JsonConvert.SerializeObject(npcSpawns, converter);

        // Assert
        Assert.DoesNotContain("Yaw", json);
        Assert.DoesNotContain("Pitch", json);
        Assert.DoesNotContain("Roll", json);
    }

    #endregion

    #region JsonDoodadSpawns Tests

    [Fact]
    public void SerializeDoodadSpawns_WritesCorrectJson()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var doodadSpawns = new JsonDoodadSpawns
        {
            Id = 42,
            UnitId = 100,
            Position = new JsonPosition { X = 500f, Y = 600f, Z = 700f }
        };

        // Act
        var json = JsonConvert.SerializeObject(doodadSpawns, converter);

        // Assert
        Assert.Contains("42", json);
        Assert.Contains("100", json);
    }

    #endregion
}
