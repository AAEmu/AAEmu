using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils.Converters;
using Newtonsoft.Json;
using Xunit;

namespace AAEmu.UnitTests.Game.Models.Json;

public class ModelsJsonConverterTests
{
    [Fact]
    public void ConvertAComplexObject_WhenYawRollPitchIsZero_ShouldIgnore()
    {
        //Arrange
        var spawnsList = new[]
        {
            new JsonNpcSpawns
            {
                Id = 1,
                UnitId = 1,
                Title = "test",
                FollowPath = "test",
                Position = new JsonPosition
                {
                    X = 1, Y = 1, Z = 1,
                    Yaw = 0,
                    Pitch = 0,
                    Roll = 0,
                },
                Scale = 1f
            }
        };
        var expected = "[{\"Id\":1,\"UnitId\":1,\"Title\":\"test\",\"FollowPath\":\"test\",\"Position\":{\"X\":1.0,\"Y\":1.0,\"Z\":1.0},\"Scale\":1.0}]";

        //Act
        var conversion = JsonConvert.SerializeObject(spawnsList, new JsonModelsConverter());

        //Assert
        Assert.Equal(expected, conversion);
    }

    [Fact]
    public void ConvertAComplexObject_WhenYawIsZero_ShouldIgnore()
    {
        //Arrange
        var spawnsList = new JsonNpcSpawns[]
        {
            new()
            {
                Id = 1,
                UnitId = 1,
                Title = "test",
                FollowPath = "test",
                Position = new JsonPosition
                {
                    X = 1f, Y = 1f, Z = 1f,
                    Roll = 30,
                    Pitch = 20,
                    Yaw = 0
                },
                Scale = 1f
            }
        };
        var expected = "[{\"Id\":1,\"UnitId\":1,\"Title\":\"test\",\"FollowPath\":\"test\",\"Position\":{\"X\":1.0,\"Y\":1.0,\"Z\":1.0,\"Roll\":30,\"Pitch\":20},\"Scale\":1.0}]";

        //Act
        var conversion = JsonConvert.SerializeObject(spawnsList, new JsonModelsConverter());

        //Assert
        Assert.Equal(expected, conversion);
    }
}
