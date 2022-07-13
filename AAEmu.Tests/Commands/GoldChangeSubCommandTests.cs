using System;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts.SubCommands.Gold;
using Moq;
using Xunit;

namespace AAEmu.Tests.Commands
{
    public class GoldChangeSubCommandTests
    {
        [Fact]
        public void SendHelpMessage_ShouldMatch()
        {
            // Arrange
            var subCommand = new GoldChangeSubCommandFake();
            var mockCharacter = new Mock<ICharacter>();

            // Act
            subCommand.BaseSendHelpMessage(mockCharacter.Object);

            // Assert
            mockCharacter.Verify(c => c.SendMessage(It.IsIn("[Gold Change] /gold <add||change||remove> <player name||target||self> <gold amount> [<silver amount>] [<copper amount>]")), Times.Once);
        }


        private class GoldChangeSubCommandFake : GoldSetSubCommand
        {
            public void BaseSendHelpMessage(ICharacter character)
            {
                base.SendHelpMessage(character);
            }
        }
    }
}
