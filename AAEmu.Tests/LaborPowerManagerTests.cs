using System.Collections.Generic;
using System.IO;
using System.Linq;
using AAEmu.Commons.IO;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Core.Managers.Id;
using AAEmu.Game.Core.Network.Connections;
using AAEmu.Game.Core.Network.Game;
using AAEmu.Game.Core.Packets.G2C;
using AAEmu.Game.Models;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AAEmu.Tests
{
    public class LaborPowerManagerTests
    {
        [Fact]
        public void LaborPowerTick_OfflineTooLong_ShouldMaxOut()
        {
            SetupPrerequisits();

            var connection = new GameConnection(
                new Commons.Network.Core.Session(
                    new Commons.Network.Core.Client(
                        new System.Net.IPAddress(0x2414188f), 21000, new GameProtocolHandler()
                    )
               ));


            GameConnectionTable.Instance.AddConnection(connection);
            var charecter = new MockCharacter(new UnitCustomModelParams(UnitCustomModelType.Hair));
            charecter.Name = "test";
            charecter.LaborPower = 0;
            charecter.IsOnline = true;
            connection.Characters.Add(0, charecter);

            LaborPowerManager.Instance.LaborPowerTick();

            var sentLaborChangePackets = charecter.SentPackets.OfType<SCCharacterLaborPowerChangedPacket>();

            Assert.Single(sentLaborChangePackets);
            Assert.Equal(5000, sentLaborChangePackets.Single().Amount);
        }

        [Fact]
        public void LaborPowerTick_OfflineTooLong_ShouldNotBeNegative()
        {
            SetupPrerequisits();

            var connection = new GameConnection(
                new Commons.Network.Core.Session(
                    new Commons.Network.Core.Client(
                        new System.Net.IPAddress(0x2414188f), 21000, new GameProtocolHandler()
                    )
               ));

            GameConnectionTable.Instance.AddConnection(connection);
            var charecter = new MockCharacter(new UnitCustomModelParams(UnitCustomModelType.Hair));
            charecter.Name = "test";
            charecter.LaborPower = 0;
            charecter.LaborPowerModified = System.DateTime.UtcNow.AddDays(-12);
            charecter.IsOnline = true;
            connection.Characters.Add(0, charecter);

            LaborPowerManager.Instance.LaborPowerTick();

            var sentLaborChangePackets = charecter.SentPackets.OfType<SCCharacterLaborPowerChangedPacket>();

            Assert.Single(sentLaborChangePackets);
            Assert.Equal(5000, sentLaborChangePackets.Single().Amount);
        }

        private void SetupPrerequisits()
        {
            var mainConfig = Path.Combine(FileManager.AppPath, "TestConfig.json");
            var configurationBuilder = new ConfigurationBuilder();
            configurationBuilder.AddJsonFile(mainConfig);
            var configurationBuilderResult = configurationBuilder.Build();
            configurationBuilderResult.Bind(AppConfiguration.Instance);

            FriendMananger.Instance.Load();
            TaskIdManager.Instance.Initialize();
            TaskManager.Instance.Initialize();
            LaborPowerManager.Instance.Initialize();
        }
    }

    public class MockCharacter : Character
    {
        public List<GamePacket> SentPackets { get; private set; } = new List<GamePacket>();
        public MockCharacter(UnitCustomModelParams modelParams) : base(modelParams)
        {

        }

        public override void SendPacket(GamePacket packet)
        {
            SentPackets.Add(packet);
        }
    }
}
