using System.Collections.Generic;
using System.Drawing;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using Moq;
using Xunit;

namespace AAEmu.Tests.Commands
{
    public class SubCommandParametersTests
    {
        [Theory]
        [InlineData("param1,param2", "test")]
        [InlineData("param1,param2,param3,param4", "test")]
        [InlineData("param1,param2,param3,param4", "test", "test2", "test3")]
        public void PreValidate_WhenRequiredStringParametersAreNotMet_ShouldSendMessage(string requiredParameters, params string[] arguments)
        {
            var parameters = new List<SubCommandParameterDefinition>();
            foreach (var parameterName in requiredParameters.Split(','))
            {
                parameters.Add(new StringSubCommandParameter(parameterName, true));
            }

            var subCommand = new SubCommandTest(parameters);
            var mockCharacter = new Mock<ICharacter>();

            subCommand.PreExecute(mockCharacter.Object, "", arguments);

            if (parameters.Count > arguments.Length)
            {
                var missingParameters = parameters.Count - arguments.Length;
                for (var i = arguments.Length; i < parameters.Count; i++)
                {
                    mockCharacter.Verify(c => c.SendMessage(It.IsIn(Color.Red), It.IsIn($"[Test] Parameter {parameters[i].Name} is required")), Times.Once);
                }
            }
        }
    }


    public class SubCommandTest : SubCommandBase
    {
        public SubCommandTest(IEnumerable<SubCommandParameterDefinition> parameterDefinitions)
        {
            Prefix = "[Test]";
            foreach (var parameterDefinition in parameterDefinitions)
            {
                AddParameter(parameterDefinition);
            }
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            
        }
    }
}
