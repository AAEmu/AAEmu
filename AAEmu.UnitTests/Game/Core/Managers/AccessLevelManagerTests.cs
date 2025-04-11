using System.Collections.Generic;
using System.Reflection;

using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models;

using Xunit;

namespace AAEmu.UnitTests.Game.Core.Managers
{
    public class AccessLevelManagerTests
    {
        private readonly AccessLevelManager _manager;

        public AccessLevelManagerTests()
        {
            _manager = new AccessLevelManager();
            ResetAppConfiguration();
        }

        private void ResetAppConfiguration()
        {
            AppConfiguration.Instance.AccessLevel?.Clear();
        }

        [Fact]
        public void GetLevel_WhenCommandNotExists_ShouldReturnDefaultLevel()
        {
            _manager.Load();
            var result = _manager.GetLevel("non_existent_command");
            Assert.Equal(100, result);
        }

        [Fact]
        public void GetLevel_WhenCommandExists_ShouldReturnCorrectLevel()
        {
            var config = AppConfiguration.Instance;
            var accessLevel = config.AccessLevel as Dictionary<string, int>;

            accessLevel.Add("test_command", 5);

            _manager.Load();
            var result = _manager.GetLevel("test_command");
            Assert.Equal(5, result);
        }

        [Fact]
        public void Load_ShouldLoadMultipleCommandsCorrectly()
        {
            var config = AppConfiguration.Instance;
            var accessLevel = config.AccessLevel as Dictionary<string, int>;

            accessLevel["cmd1"] = 1;
            accessLevel["cmd2"] = 2;
            accessLevel["cmd3"] = 3;

            _manager.Load();
            Assert.Equal(1, _manager.GetLevel("cmd1"));
            Assert.Equal(2, _manager.GetLevel("cmd2"));
            Assert.Equal(3, _manager.GetLevel("cmd3"));
        }

        [Fact]
        public void Load_WhenDuplicateCommands_ShouldOverwriteLevel()
        {
            var config = AppConfiguration.Instance;
            var accessLevel = config.AccessLevel as Dictionary<string, int>;

            accessLevel["duplicate"] = 5;
            accessLevel["duplicate"] = 10;

            _manager.Load();
            Assert.Equal(10, _manager.GetLevel("duplicate"));
        }

        [Fact]
        public void Load_WhenEmptyConfig_ShouldNotLoadCommands()
        {
            _manager.Load();
            Assert.Equal(100, _manager.GetLevel("any_command"));
        }
    }
}
