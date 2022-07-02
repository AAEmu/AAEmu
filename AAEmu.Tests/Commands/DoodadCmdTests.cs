using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Units;
using AAEmu.Game.Scripts.Commands;
using Moq;
using Xunit;

namespace AAEmu.Tests.Commands
{
    public class DoodadCmdTests
    {
        [Fact]
        public void Execute_WhenSendingOnlyDoodad_ShouldNotThrowException()
        {
            var mockUnitCustomModelParams = new Mock<UnitCustomModelParams>(UnitCustomModelType.None);
            var mockCharacter = new Mock<Character>(mockUnitCustomModelParams.Object);

            var doodadCmd = new DoodadCmd();
            doodadCmd.Execute(mockCharacter.Object, new string[] { "doodad" });
        }
    }
}
