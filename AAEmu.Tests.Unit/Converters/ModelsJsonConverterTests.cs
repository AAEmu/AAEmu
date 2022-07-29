using AAEmu.Game.Models.Json;
using AAEmu.Game.Utils.Converters;
using Newtonsoft.Json;
using Xunit;

namespace AAEmu.Tests.Unit.Converters
{
    public class ModelsJsonConverterTests
    {
        [Fact]
        public void ConvertAComplexObject_WhenYawRollPitchIsZero_ShouldIgnore()
        {
            //Arrange
            var spawnsList = new JsonNpcSpawns[]
            {
                new JsonNpcSpawns
                {
                    UnitId = 1,
                    Id = 1,
                    Position = new JsonPosition
                    {
                        X = 1, Y = 1, Z = 1,
                        Yaw = 0,
                        Pitch = 0,
                        Roll = 0,
                    }
                }
            };
            var expected = "[{\"Id\":1,\"UnitId\":1,\"Position\":{\"X\":1.0,\"Y\":1.0,\"Z\":1.0}}]";

            //Act
            var conversion =  JsonConvert.SerializeObject(spawnsList, new JsonModelsConverter());
            
            //Assert
            Assert.Equal(expected, conversion);
        }

        [Fact]
        public void ConvertAComplexObject_WhenYawIsZero_ShouldIgnore()
        {
            //Arrange
            var spawnsList = new JsonNpcSpawns[]
            {
                new JsonNpcSpawns
                {
                    UnitId = 1,
                    Id = 1,
                    Position = new JsonPosition
                    {
                        X = 1, Y = 1, Z = 1,
                        Yaw = 0,
                        Pitch = 10,
                        Roll = 20,
                    }
                }
            };
            var expected = "[{\"Id\":1,\"UnitId\":1,\"Position\":{\"X\":1.0,\"Y\":1.0,\"Z\":1.0,\"Roll\":20,\"Pitch\":10}}]";

            //Act
            var conversion = JsonConvert.SerializeObject(spawnsList, new JsonModelsConverter());

            //Assert
            Assert.Equal(expected, conversion);
        }
    }
}
