using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils.Converters;
using Newtonsoft.Json;

namespace AAEmu.UnitTests.Game.Models.Json;

public class ModelsJsonConverterTests
{
    #region Constructor Tests

    [Test]
    public async Task Constructor_Default_CreatesInstanceWithConverters()
    {
        // Act
        var converter = new JsonModelsConverter();

        // Assert
        await Assert.That(converter).IsNotNull();
    }

    #endregion

    #region CanConvert Tests

    [Test]
    [Arguments(typeof(JsonPosition), true)]
    [Arguments(typeof(JsonQuestSphere), true)]
    [Arguments(typeof(JsonDoodadSpawns), true)]
    [Arguments(typeof(string), false)]
    [Arguments(typeof(int), false)]
    [Arguments(typeof(object), false)]
    public async Task CanConvert_ReturnsCorrectValue(Type objectType, bool expected)
    {
        // Arrange
        var converter = new JsonModelsConverter();

        // Act
        var result = converter.CanConvert(objectType);

        // Assert
        await Assert.That(result).IsEqualTo(expected);
    }

    #endregion

    #region AddConverter Tests

    [Test]
    public async Task AddConverter_WithValidTypes_AddsConverter()
    {
        // Arrange
        var converter = new JsonModelsConverter();

        // This tests the internal behavior - we verify by checking CanConvert works
        // Act & Assert - should not throw
        var canConvertPosition = converter.CanConvert(typeof(JsonPosition));
        await Assert.That(canConvertPosition).IsTrue();
    }

    #endregion

    #region WriteJson Tests

    [Test]
    public async Task WriteJson_WithJsonPosition_WritesCorrectJson()
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
        await Assert.That(json).Contains("100.5");
        await Assert.That(json).Contains("200.5");
        await Assert.That(json).Contains("300.5");
    }

    [Test]
    public async Task WriteJson_WithRotationValues_WritesRotation()
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
        await Assert.That(json).Contains("45");
        await Assert.That(json).Contains("30");
        await Assert.That(json).Contains("15");
    }

    [Test]
    public async Task WriteJson_WithZeroRotation_OmitsRotationFields()
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
        await Assert.That(json).DoesNotContain("Yaw");
        await Assert.That(json).DoesNotContain("Pitch");
        await Assert.That(json).DoesNotContain("Roll");
    }

    #endregion

    #region Array Serialization Tests

    [Test]
    public async Task SerializeObject_WithArrayOfPositions_WritesCorrectJson()
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
        await Assert.That(json).Contains("1");
        await Assert.That(json).Contains("2");
        await Assert.That(json).Contains("3");
        await Assert.That(json).Contains("4");
        await Assert.That(json).Contains("5");
        await Assert.That(json).Contains("6");
    }

    #endregion

    #region ReadJson Tests

    [Test]
    public async Task ReadJson_WithValidJsonPosition_ReturnsPosition()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var json = "{\"X\":10.5,\"Y\":20.5,\"Z\":30.5}";

        // Act
        var result = JsonConvert.DeserializeObject<JsonPosition>(json, converter);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.X).IsEqualTo(10.5f);
        await Assert.That(result.Y).IsEqualTo(20.5f);
        await Assert.That(result.Z).IsEqualTo(30.5f);
    }

    [Test]
    public async Task ReadJson_WithRotation_ReturnsPositionWithRotation()
    {
        // Arrange
        var converter = new JsonModelsConverter();
        var json = "{\"X\":1,\"Y\":2,\"Z\":3,\"Yaw\":45,\"Pitch\":30,\"Roll\":15}";

        // Act
        var result = JsonConvert.DeserializeObject<JsonPosition>(json, converter);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.Yaw).IsEqualTo(45);
        await Assert.That(result.Pitch).IsEqualTo(30);
        await Assert.That(result.Roll).IsEqualTo(15);
    }

    #endregion

    #region Edge Cases

    [Test]
    [Arguments("{\"X\":0,\"Y\":0,\"Z\":0}", 0, 0, 0)]
    [Arguments("{\"X\":-100.5,\"Y\":-200.5,\"Z\":-300.5}", -100.5f, -200.5f, -300.5f)]
    [Arguments("{\"X\":1.23456789,\"Y\":2.34567890,\"Z\":3.45678901}", 1.23456789f, 2.34567890f, 3.45678901f)]
    public async Task ReadJson_WithVariousValues_ReturnsCorrectPosition(string json, float expectedX, float expectedY, float expectedZ)
    {
        // Arrange
        var converter = new JsonModelsConverter();

        // Act
        var result = JsonConvert.DeserializeObject<JsonPosition>(json, converter);

        // Assert
        await Assert.That(result).IsNotNull();
        await Assert.That(result.X).IsEqualTo(expectedX);
        await Assert.That(result.Y).IsEqualTo(expectedY);
        await Assert.That(result.Z).IsEqualTo(expectedZ);
    }

    #endregion

    #region JsonDoodadSpawns Tests

    [Test]
    public async Task DeserializeQuestSphere_ReadsAllFields()
    {
        var converter = new JsonModelsConverter();
        const string json = "{\"Id\":5,\"QuestId\":17,\"SphereId\":23,\"Radius\":8.5,\"Position\":{\"X\":1,\"Y\":2,\"Z\":3}}";

        var sphere = JsonConvert.DeserializeObject<JsonQuestSphere>(json, converter);

        await Assert.That(sphere).IsNotNull();
        await Assert.That(sphere.Id).IsEqualTo(5u);
        await Assert.That(sphere.QuestId).IsEqualTo(17u);
        await Assert.That(sphere.SphereId).IsEqualTo(23u);
        await Assert.That(sphere.Radius).IsEqualTo(8.5f);
        await Assert.That(sphere.Position.Z).IsEqualTo(3f);
    }

    [Test]
    public async Task SerializeDoodadSpawns_WritesCorrectJson()
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
        await Assert.That(json).Contains("42");
        await Assert.That(json).Contains("100");
    }

    [Test]
    public async Task DoodadSpawns_RoundTripPreservesRelatedIdsAndPosition()
    {
        var converter = new JsonModelsConverter();
        var source = new JsonDoodadSpawns
        {
            Id = 42,
            UnitId = 100,
            Title = "Pulse trigger",
            RelatedIds = [7, 9],
            Position = new JsonPosition { X = 5, Y = 6, Z = 7 },
            FuncGroupId = 11,
            Scale = 1.25f
        };

        var json = JsonConvert.SerializeObject(source, converter);
        var result = JsonConvert.DeserializeObject<JsonDoodadSpawns>(json, converter);

        await Assert.That(result).IsNotNull();
        await Assert.That(result.RelatedIds).IsEquivalentTo(new uint[] { 7, 9 });
        await Assert.That(result.Position.Y).IsEqualTo(6f);
        await Assert.That(result.FuncGroupId).IsEqualTo(11u);
        await Assert.That(result.Scale).IsEqualTo(1.25f);
    }

    #endregion
}
