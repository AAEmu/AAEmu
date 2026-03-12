using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;
using Microsoft.Extensions.Options;

namespace AAEmu.UnitTests.Game.Core.Managers
{
    public class AccessLevelManagerTests
    {
        private AccessLevelManager _manager;
        private AppConfiguration _config;

        [Before(Test)]
        public void Setup()
        {
            _config = new AppConfiguration { AccessLevel = new Dictionary<string, int>() };
            _manager = new AccessLevelManager(Options.Create(_config));
        }

        [Test]
        public async Task GetLevel_WhenCommandNotExists_ShouldReturnDefaultLevel()
        {
            _manager.Load();
            var result = _manager.GetLevel("non_existent_command");
            await Assert.That(result).IsEqualTo(100);
        }

        [Test]
        public async Task GetLevel_WhenCommandExists_ShouldReturnCorrectLevel()
        {
            _config.AccessLevel["test_command"] = 5;

            _manager.Load();
            var result = _manager.GetLevel("test_command");
            await Assert.That(result).IsEqualTo(5);
        }

        [Test]
        public async Task Load_ShouldLoadMultipleCommandsCorrectly()
        {
            _config.AccessLevel["cmd1"] = 1;
            _config.AccessLevel["cmd2"] = 2;
            _config.AccessLevel["cmd3"] = 3;

            _manager.Load();
            await Assert.That(_manager.GetLevel("cmd1")).IsEqualTo(1);
            await Assert.That(_manager.GetLevel("cmd2")).IsEqualTo(2);
            await Assert.That(_manager.GetLevel("cmd3")).IsEqualTo(3);
        }

        [Test]
        public async Task Load_WhenDuplicateCommands_ShouldOverwriteLevel()
        {
            _config.AccessLevel["duplicate"] = 5;
            _config.AccessLevel["duplicate"] = 10;

            _manager.Load();
            await Assert.That(_manager.GetLevel("duplicate")).IsEqualTo(10);
        }

        [Test]
        public async Task Load_WhenEmptyConfig_ShouldNotLoadCommands()
        {
            _manager.Load();
            await Assert.That(_manager.GetLevel("any_command")).IsEqualTo(100);
        }
    }
}
