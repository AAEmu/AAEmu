using System;
using System.Collections.Generic;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.IO;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using Moq;
using NLog;
using Xunit;

namespace AAEmu.Tests
{
    public class LaborPowerManagerTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void LaborPowerTick_WhenNoCharactersOnline_DoNothing(int numberOfflineCharacters)
        {
            //Arrange
            var sut = SetupManager(out _, out var mockLogger, out _, out var mockGameConnectionTable, out _);
            var mockGameConnection = new Mock<IGameConnection>();

            var offlineCharacters = new Dictionary<uint, ICharacter>();
            for (uint i = 0; i < numberOfflineCharacters; i++)
            {
                var character = new Character(new UnitCustomModelParams())
                {
                    IsOnline = false
                };
                offlineCharacters.Add(i, character);
            }

            mockGameConnection.SetupGet(gc => gc.Characters).Returns(offlineCharacters);
            var gameConnections = new List<IGameConnection> {
               mockGameConnection.Object
            };

            mockGameConnectionTable.Setup(lm => lm.GetConnections()).Returns(gameConnections);

            //Act
            sut.LaborPowerTick();

            //Assert
            mockLogger.Verify(l => l.Info(It.IsAny<string>()), Times.Never);
            mockLogger.Verify(l => l.Debug(It.IsAny<string>()), Times.Never);
        }


        [Theory]
        [InlineData(6000)]
        [InlineData(5001)]
        public void LaborPowerTick_WithLaborPowerAboveUpLimit_DoNothing(short laborPower)
        {
            //Arrange
            var sut = SetupManager(out _, out var mockLogger, out _, out var mockGameConnectionTable, out _);
            var mockGameConnection = new Mock<IGameConnection>();

            var offlineCharacters = new Dictionary<uint, ICharacter>()
            {
                {
                    0,
                    new Character(new UnitCustomModelParams())
                    {
                        IsOnline = false,
                        LaborPower = laborPower
                    }
                }
            };

            mockGameConnection.SetupGet(gc => gc.Characters).Returns(offlineCharacters);
            var gameConnections = new List<IGameConnection> {
               mockGameConnection.Object
            };

            mockGameConnectionTable.Setup(lm => lm.GetConnections()).Returns(gameConnections);

            //Act
            sut.LaborPowerTick();

            //Assert
            mockLogger.Verify(l => l.Info(It.IsAny<string>()), Times.Never);
            mockLogger.Verify(l => l.Debug(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public void LaborPowerTick_WhenMoreLaborThanShortMax_ShouldMaxCurrentCharacterLabor()
        {
            //Arrange
            var sut = SetupManager(out _, out var mockLogger, out _, out var mockGameConnectionTable, out var mockDateTimeManager);
            var mockGameConnection = new Mock<IGameConnection>();
            mockDateTimeManager.SetupGet(d => d.UtcNow).Returns(new DateTime(2010, 10, 25));
            var dateLaborPowerModified = new DateTime(2010, 10, 9);

            var mockCharacter = new Mock<ICharacter>();
            mockCharacter.SetupGet(d => d.IsOnline).Returns(true);
            mockCharacter.SetupGet(d => d.Name).Returns("Test Character");
            mockCharacter.SetupGet(d => d.LaborPower).Returns(200);
            mockCharacter.SetupGet(d => d.LaborPowerModified).Returns(dateLaborPowerModified);

            var onlineCharacters = new Dictionary<uint, ICharacter>()
            {
                {
                    0, mockCharacter.Object
                }
            };

            mockGameConnection.SetupGet(gc => gc.Characters).Returns(onlineCharacters);
            var gameConnections = new List<IGameConnection> {
               mockGameConnection.Object
            };

            mockGameConnectionTable.Setup(lm => lm.GetConnections()).Returns(gameConnections);

            //Act
            sut.LaborPowerTick();

            //Assert
            mockCharacter.Verify(c => c.ChangeLabor(It.IsIn<short>(4800), It.IsIn(0)), Times.Once());
            mockLogger.Verify(l => l.Debug(It.IsAny<string>()), Times.Once);
            mockLogger.Verify(l => l.Info(It.IsAny<string>()), Times.Never);
        }

        private LaborPowerManager SetupManager(out Mock<ILogManager> mockLogManager, out Mock<ILogger> mockLogger, out Mock<ITaskManager> mockTaskManager, out Mock<IGameConnectionTable> mockGameConnectionTable, out Mock<IDateTimeManager> mockDateTimeManager)
        {
            mockLogManager = new Mock<ILogManager>();
            mockTaskManager = new Mock<ITaskManager>();
            mockLogger = new Mock<ILogger>();
            mockGameConnectionTable = new Mock<IGameConnectionTable>();
            mockDateTimeManager = new Mock<IDateTimeManager>();
            mockLogManager.Setup(lm => lm.GetCurrentLogger()).Returns(mockLogger.Object);

            return new LaborPowerManager(mockLogManager.Object, mockTaskManager.Object, mockGameConnectionTable.Object, mockDateTimeManager.Object);
        }
    }
}
