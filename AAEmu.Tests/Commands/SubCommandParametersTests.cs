using System;
using System.Collections.Generic;
using System.Text;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using Moq;
using Xunit;

namespace AAEmu.Tests.Commands
{
    public class SubCommandParametersTests
    {
        [Fact]
        public void Test_Parameter()
        {
            var parameter1 = new StringSubCommandParameter("param1", true);
            var parameter2 = new StringSubCommandParameter("param2", true);


            var subCommand = new SubCommandTest(new List<SubCommandParameterDefinition>() { parameter1, parameter2 });
            var mockCharacter = new Mock<ICharacter>();

            subCommand.PreExecute(mockCharacter.Object, "", new string[] { "test" });
        }
    }


    public class SubCommandTest : SubCommandBase
    {
        public SubCommandTest(IEnumerable<SubCommandParameterDefinition> parameterDefinitions)
        {
            foreach (var parameterDefinition in parameterDefinitions)
            {
                AddParameter(parameterDefinition);
            }
        }

        public override void Execute(ICharacter character, string triggerArgument, string[] args)
        {
            
        }
    }
}
