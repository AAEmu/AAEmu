using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using Moq;
using Xunit;

namespace AAEmu.Tests.Commands
{
    public class SubCommandBaseTests
    {
        [Theory]
        [InlineData("param1,param2", "test")]
        [InlineData("param1,param2,param3,param4", "test")]
        [InlineData("param1,param2,param3,param4", "test", "test2", "test3")]
        public void PreValidate_WhenRequiredStringParametersAreNotMet_PreValidateShouldSendMessage(string requiredParameters, params string[] arguments)
        {
            // Arrange
            var parameters = new List<SubCommandParameterBase>();
            foreach (var parameterName in requiredParameters.Split(','))
            {
                parameters.Add(new StringSubCommandParameter(parameterName, true));
            }

            var subCommand = new SubCommandTest(parameters);
            var mockCharacter = new Mock<ICharacter>();

            // Act
            subCommand.PreExecute(mockCharacter.Object, "", arguments);

            // Assert
            Assert.False(subCommand.Executed);
            if (parameters.Count > arguments.Length)
            {
                var missingParameters = parameters.Count - arguments.Length;
                for (var i = arguments.Length; i < parameters.Count; i++)
                {
                    mockCharacter.Verify(c => c.SendMessage(It.IsIn(Color.Red), It.IsIn($"[Test] Parameter {parameters[i].Name} is required")), Times.Once);
                }
            }
        }

        [Theory]
        [InlineData("required-prefix-x", "x=test", "x=test2duplicate")]
        [InlineData("optional-prefix-y", "y=test", "y=test2duplicate")]
        [InlineData("optional-prefix-name", "y=test", "y=test2duplicate", "name=ok", "name=duplicate")]
        public void PreValidate_WhenDuplicatedPrefixParameters_PreValidateShouldSendMessage(string prefixParameters, params string[] arguments)
        {
            // Arrange
            var parameters = new List<SubCommandParameterBase>();
            var prefixParametersArray = prefixParameters.Split(',');
            foreach (var parameterName in prefixParametersArray)
            {
                var required = parameterName.Contains("required");
                var prefix = parameterName.Split("prefix-").Last();

                parameters.Add(new StringSubCommandParameter(parameterName, required, prefix));
            }

            var subCommand = new SubCommandTest(parameters);
            var mockCharacter = new Mock<ICharacter>();

            // Act
            subCommand.PreExecute(mockCharacter.Object, "", arguments);

            // Assert
            Assert.False(subCommand.Executed);
            mockCharacter.Verify(c => c.SendMessage(It.IsIn(Color.Red), It.IsIn($"[Test] Parameter prefix {parameters[0].Prefix} is duplicated")), Times.Once);
        }
        
        [Theory]
        [InlineData("param1,param2", "test", "test2")]
        [InlineData("param1,param2,param3,param4", "test", "test2", "test3", "test4")]
        public void PreValidate_WhenRequiredStringParametersAreMet_ShouldExecuteWithParameters(string requiredParameters, params string[] arguments)
        {
            // Arrange
            var parameters = new List<SubCommandParameterBase>();
            foreach (var parameterName in requiredParameters.Split(','))
            {
                parameters.Add(new StringSubCommandParameter(parameterName, true));
            }

            var subCommand = new SubCommandTest(parameters);
            var mockCharacter = new Mock<ICharacter>();

            // Act
            subCommand.PreExecute(mockCharacter.Object, "", arguments);

            // Assert
            Assert.True(subCommand.Executed);
            Assert.Equal(arguments.Length, subCommand.Parameters.Count);
            var counter = 0;
            foreach(var parameterKeyValue in subCommand.Parameters)
            {
                Assert.Equal(parameters[counter].Name, parameterKeyValue.Key);
                Assert.Equal(arguments[counter], parameterKeyValue.Value.Value);
                Assert.True(parameterKeyValue.Value.IsValid);
                counter++;
            }
            mockCharacter.Verify(c => c.SendMessage(It.IsAny<Color>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("param1", "param2", "test")]
        [InlineData("param1,param2", "param3,param4", "test", "test2")]
        [InlineData("param1,param2", "param3,param4", "test", "test2", "test3")]
        [InlineData("param1,param2", "param3,param4", "test", "test2", "test3", "test4")]
        [InlineData("param1,param2", "param3,param4", "test", "test2", "test3", "test4", "test5")]
        [InlineData("param1,param2", "param3,param4,param5", "test", "test2", "test3", "test4", "test5")]
        [InlineData("param1,param2", "param3,param4,param5", "test", "test2", "test3", "test4", "test5", "test6", "test7")]
        public void LoadParametersValues_WhenOptionalParameterAreNotPresent_ShouldExecuteWithOnlyProvidedParameters(string requiredParameters, string optionalParameters, params string[] arguments)
        {
            // Arrange
            var parameters = new List<SubCommandParameterBase>();
            var requiredParametersArray = requiredParameters.Split(',');
            foreach (var parameterName in requiredParametersArray)
            {
                parameters.Add(new StringSubCommandParameter(parameterName, true));
            }
            var optionalParametersArray = optionalParameters.Split(',');
            foreach (var parameterName in optionalParametersArray)
            {
                parameters.Add(new StringSubCommandParameter(parameterName, false));
            }

            var subCommand = new SubCommandTest(parameters);
            var mockCharacter = new Mock<ICharacter>();
            var parametersToIgnore = 0;
            if (arguments.Length > optionalParametersArray.Length + requiredParametersArray.Length)
            {
                parametersToIgnore = arguments.Length - optionalParametersArray.Length - requiredParametersArray.Length;
            }
            var optionalArgumentsProvided = arguments.Length - requiredParametersArray.Length;
            var expectedNumberOfParameters = requiredParametersArray.Length + optionalArgumentsProvided - parametersToIgnore;

            // Act
            subCommand.PreExecute(mockCharacter.Object, "", arguments);

            // Assert
            Assert.True(subCommand.Executed);

            Assert.Equal(expectedNumberOfParameters, subCommand.Parameters.Count);
            var counter = 0;
            foreach (var parameterKeyValue in subCommand.Parameters)
            {
                Assert.Equal(parameters[counter].Name, parameterKeyValue.Key);
                Assert.Equal(arguments[counter], parameterKeyValue.Value.Value);
                Assert.True(parameterKeyValue.Value.IsValid);
                counter++;
            }
            mockCharacter.Verify(c => c.SendMessage(It.IsAny<Color>(), It.IsAny<string>()), Times.Never);
        }
        
        [Theory]
        [InlineData("test", "valid1", "valid2")]
        [InlineData("test", "test1")]
        public void PreValidate_WhenRangedStringParameterAreNotMet_PreValidateShouldSendMessage(string argumentValue, params string[] validValues)
        {
            //Arrange
            var parameter = new StringSubCommandParameter("param1", true, validValues);
            var subCommand = new SubCommandTest(new[] { parameter });
            var mockCharacter = new Mock<ICharacter>();

            // Act
            subCommand.PreExecute(mockCharacter.Object, "", new[] { argumentValue });

            // Assert
            Assert.False(subCommand.Executed);
            mockCharacter.Verify(c => c.SendMessage(It.IsIn(Color.Red), It.IsIn($"[Test] Parameter {parameter.Name} only accepts those values: {string.Join("||", validValues)}")), Times.Once);
        }

        [Theory]
        [InlineData("test", "test", "test2", "test3")]
        public void PreValidate_WhenRangedStringParameterAreMet_ShouldExecute(string argumentValue, params string[] validValues)
        {
            //Arrange
            var parameter = new StringSubCommandParameter("param1", true, validValues);
            var subCommand = new SubCommandTest(new[] { parameter });
            var mockCharacter = new Mock<ICharacter>();

            // Act
            subCommand.PreExecute(mockCharacter.Object, "", new[] { argumentValue });

            // Assert
            Assert.True(subCommand.Executed);
            Assert.Single(subCommand.Parameters);
            Assert.Equal(argumentValue, subCommand.Parameters["param1"].Value);
            
            mockCharacter.Verify(c => c.SendMessage(It.IsAny<Color>(), It.IsAny<string>()), Times.Never);
        }

        [Theory]
        [InlineData("req1", "opt2", "required-prefix-x", "x=test", "firstRequired", "SecondOptional", "shouldIgnoreMe")]
        [InlineData("req1", "opt2", "required-prefix-x", "firstRequired", "x=test", "SecondOptional", "shouldIgnoreMe", "shouldIgnoreMe")]
        [InlineData("req1", "opt2", "required-prefix-x", "firstRequired", "SecondOptional", "x=test", "shouldIgnoreMe", "shouldIgnoreMe", "y=z")]
        [InlineData("req1", "opt2", "required-prefix-x", "firstRequired", "SecondOptional", "x=test", "shouldIgnoreMe", "shouldIgnoreMe", "y=z", "ignoreMe")]
        public void LoadParametersValues_WhenPrefixArgumentsAnyOrder_ShouldExecute(string requiredParameters, string optionalParameters, string prefixParameters, params string[] arguments)
        {
            // Arrange
            var parameters = new List<SubCommandParameterBase>();
            var requiredParametersArray = requiredParameters.Split(',');
            foreach (var parameterName in requiredParametersArray)
            {
                parameters.Add(new StringSubCommandParameter(parameterName, true));
            }
            var optionalParametersArray = optionalParameters.Split(',');
            foreach (var parameterName in optionalParametersArray)
            {
                parameters.Add(new StringSubCommandParameter(parameterName, false));
            }
            var prefixParametersArray = prefixParameters.Split(',');
            foreach (var parameterName in prefixParametersArray)
            {
                var required = parameterName.Contains("required");
                var prefix = parameterName.Split("prefix-").Last();

                parameters.Add(new StringSubCommandParameter(parameterName, required, prefix));
            }
            
            var subCommand = new SubCommandTest(parameters);
            var mockCharacter = new Mock<ICharacter>();

            // Act
            subCommand.PreExecute(mockCharacter.Object, "", arguments);

            // Assert
            Assert.True(subCommand.Executed);

            Assert.Equal(3, subCommand.Parameters.Count);
            
            var counter = 0;
            var nonPrefixCounter = 0;
            foreach (var argument in arguments)
            {
                if (argument.IndexOf('=') > -1)
                {
                    if (parameters.Any(p => p.Prefix is not null && p.Prefix == argument.Split('=')[0])) 
                    {
                        // Any parameters configured as prefix where the argument matches should be present in the subcommand parameters
                        Assert.Contains(subCommand.Parameters, p => p.Value.Value.ToString() == argument.Split('=')[1]);
                    }
                    else
                    {
                        // Any parameters that are not by prefix should not have prefixed argument values
                        Assert.DoesNotContain(subCommand.Parameters, p => p.Value.Value.ToString() == argument.Split('=')[1]);
                    }
                }
                else
                {
                    if (nonPrefixCounter < optionalParametersArray.Length + requiredParametersArray.Length)
                    {
                        // Any argument that not match the prefix pattern should be matching the non prefix parameters in order
                        // If the nonprefix arguments matched more the number of nonprefix parameters we don't expect the parameters to contains any of those values
                        Assert.Contains(subCommand.Parameters, p => p.Value.Value.ToString() == argument);
                        nonPrefixCounter++;
                    }
                }
                counter++;
            }
            mockCharacter.Verify(c => c.SendMessage(It.IsAny<Color>(), It.IsAny<string>()), Times.Never);
        }
    }
    public class SubCommandTest : SubCommandBase
    {
        public IDictionary<string, ParameterValue> Parameters { get; private set; }
        public bool Executed { get; private set; }
        public SubCommandTest(IEnumerable<SubCommandParameterBase> parameterDefinitions)
        {
            Prefix = "[Test]";
            foreach (var parameterDefinition in parameterDefinitions)
            {
                AddParameter(parameterDefinition);
            }
        }

        public override void Execute(ICharacter character, string triggerArgument, IDictionary<string, ParameterValue> parameters)
        {
            Executed = true;
            Parameters = parameters;
        }
    }
}
