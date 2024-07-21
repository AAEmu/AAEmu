#pragma warning disable CS0618 // Type or member is obsolete

using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using Moq;
using Xunit;

namespace AAEmu.UnitTests.Game.Utils.Scripts.SubCommands;

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
            parameters.Add(new StringSubCommandParameter(parameterName, null, true));
        }

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", arguments, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.False(subCommand.Executed);
        if (parameters.Count > arguments.Length)
        {
            var missingParameters = parameters.Count - arguments.Length;
            for (var i = arguments.Length; i < parameters.Count; i++)
            {
                // TODO: Fix this test
                // mockCharacter.Verify(c => c.SendMessage(It.IsIn(ChatType.System), It.IsIn($"[Test] Parameter {parameters[i].Name} is required"), It.IsIn(Color.Red)), Times.Once);
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

            parameters.Add(new StringSubCommandParameter(parameterName, null, required, prefix));
        }

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", arguments, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.False(subCommand.Executed);

        // TODO: Fix this test
        // mockCharacter.Verify(c => c.SendMessage(It.IsIn(ChatType.System), It.IsIn($"[Test] Parameter prefix {parameters[0].Prefix} is duplicated"), It.IsIn(Color.Red)), Times.Once);
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
            parameters.Add(new StringSubCommandParameter(parameterName, null, true));
        }

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", arguments, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.True(subCommand.Executed);
        Assert.Equal(arguments.Length, subCommand.Parameters.Count);
        var counter = 0;
        foreach (var parameterKeyValue in subCommand.Parameters)
        {
            Assert.Equal(parameters[counter].Name, parameterKeyValue.Key);
            Assert.Equal(arguments[counter], parameterKeyValue.Value);
            counter++;
        }
        mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsAny<string>(), It.IsAny<Color>()), Times.Never);
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
            parameters.Add(new StringSubCommandParameter(parameterName, null, true));
        }
        var optionalParametersArray = optionalParameters.Split(',');
        foreach (var parameterName in optionalParametersArray)
        {
            parameters.Add(new StringSubCommandParameter(parameterName, null, false));
        }

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();
        var parametersToIgnore = 0;
        if (arguments.Length > optionalParametersArray.Length + requiredParametersArray.Length)
        {
            parametersToIgnore = arguments.Length - optionalParametersArray.Length - requiredParametersArray.Length;
        }
        var optionalArgumentsProvided = arguments.Length - requiredParametersArray.Length;
        var expectedNumberOfParameters = requiredParametersArray.Length + optionalArgumentsProvided - parametersToIgnore;

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", arguments, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.True(subCommand.Executed);

        Assert.Equal(expectedNumberOfParameters, subCommand.Parameters.Count);
        var counter = 0;
        foreach (var parameterKeyValue in subCommand.Parameters)
        {
            Assert.Equal(parameters[counter].Name, parameterKeyValue.Key);
            Assert.Equal(arguments[counter], parameterKeyValue.Value);
            counter++;
        }
        mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsAny<string>(), It.IsAny<Color>()), Times.Never);
    }

    [Theory]
    [InlineData("test", "valid1", "valid2")]
    [InlineData("test", "test1")]
    public void PreValidate_WhenRangedStringParameterAreNotMet_PreValidateShouldSendMessage(string argumentValue, params string[] validValues)
    {
        // Arrange
        var parameter = new StringSubCommandParameter("param1", "parameter 1", true, validValues);
        var subCommand = new SubCommandFake(new[] { parameter });
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", new[] { argumentValue }, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.False(subCommand.Executed);

        // TODO: fix this test
        // mockCharacter.Verify(c => c.SendMessage(It.IsIn<ChatType>(), It.IsIn($"[Test] Parameter [{parameter.DisplayName}] only accepts: {string.Join("||", validValues)}"), It.IsIn(Color.Red)), Times.Once);
    }

    [Theory]
    [InlineData("test", "test", "test2", "test3")]
    public void PreValidate_WhenRangedStringParameterAreMet_ShouldExecute(string argumentValue, params string[] validValues)
    {
        // Arrange
        var parameter = new StringSubCommandParameter("param1", "parameter 1", true, validValues);
        var subCommand = new SubCommandFake(new[] { parameter });
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", new[] { argumentValue }, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.True(subCommand.Executed);
        Assert.Single(subCommand.Parameters);
        Assert.Equal(argumentValue, subCommand.Parameters["param1"]);

        mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsAny<string>(), It.IsAny<Color>()), Times.Never);
    }

    [Theory]
    [InlineData("req1", "opt2", "required-prefix-x", "x=test", "firstRequired", "SecondOptional", "shouldIgnoreMe")]
    [InlineData("req1", "opt2", "required-prefix-x", "firstRequired", "x=test", "SecondOptional", "shouldIgnoreMe", "shouldIgnoreMe")]
    [InlineData("req1", "opt2", "required-prefix-x", "firstRequired", "SecondOptional", "x=test", "shouldIgnoreMe", "shouldIgnoreMe", "y=z")]
    [InlineData("req1", "opt2", "required-prefix-x", "firstRequired", "SecondOptional", "x=test", "shouldIgnoreMe", "shouldIgnoreMe", "y=z", "ignoreMe")]
    public void LoadParametersValues_WhenMixedNonPrefixAndAnyOrderPrefix_ShouldExecute(string requiredParameters, string optionalParameters, string prefixParameters, params string[] arguments)
    {
        // Arrange
        var parameters = new List<SubCommandParameterBase>();
        var requiredParametersArray = requiredParameters.Split(',');
        foreach (var parameterName in requiredParametersArray)
        {
            parameters.Add(new StringSubCommandParameter(parameterName, null, true));
        }
        var optionalParametersArray = optionalParameters.Split(',');
        foreach (var parameterName in optionalParametersArray)
        {
            parameters.Add(new StringSubCommandParameter(parameterName, null, false));
        }
        var prefixParametersArray = prefixParameters.Split(',');
        foreach (var parameterName in prefixParametersArray)
        {
            var required = parameterName.Contains("required");
            var prefix = parameterName.Split("prefix-").Last();

            parameters.Add(new StringSubCommandParameter(parameterName, null, required, prefix));
        }

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", arguments, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.True(subCommand.Executed);

        Assert.Equal(3, subCommand.Parameters.Count);

        var counter = 0;
        var nonPrefixCounter = 0;
        foreach (var argument in arguments)
        {
            if (IsPrefixed(argument))
            {
                if (AnyPrefixedParameterThatMatchesThePrefixedArgument(parameters, argument))
                {
                    // Any parameters configured as prefix where the argument matches should be present in the subcommand parameters
                    Assert.Contains(subCommand.Parameters, p => p.Value.ToString() == argument.Split('=')[1]);
                }
                else
                {
                    // Any parameters that are not by prefix should not have prefixed argument values
                    Assert.DoesNotContain(subCommand.Parameters, p => p.Value.ToString() == argument.Split('=')[1]);
                }
            }
            else
            {
                if (AnyNonPrefixParameterMatchInOrder(requiredParametersArray, optionalParametersArray, nonPrefixCounter))
                {
                    // Any argument that not match the prefix pattern should be matching the non prefix parameters in order
                    // If the nonprefix arguments matched more the number of nonprefix parameters we don't expect the parameters to contains any of those values
                    Assert.Contains(subCommand.Parameters, p => p.Value.ToString() == argument);
                    nonPrefixCounter++;
                }
            }
            counter++;
        }
        mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsAny<string>(), It.IsAny<Color>()), Times.Never);
    }

    [Theory]
    [InlineData("required-long-prefix-w,optional-int-prefix-x,required-float,required-string", "required-float", -13.23F, "x=12", "w=100", "-13.23", "requiredstring", "extrastring")]
    [InlineData("required-long-prefix-w,optional-int-prefix-x,required-float,required-string", "required-float", 123.54F, "w=100", "123.54", "requiredstring", "extrastring")]
    [InlineData("required-float-prefix-yaw,optional-int-prefix-x,required-byte,optional-string", "required-float-prefix-yaw", 185.4323F, "yaw=185.4323", "200")]
    public void LoadParametersValues_MixedParameters_ShouldExecute(string parametersPattern, string expectedParameterName, object expectedParameterValue, params string[] arguments)
    {
        // Arrange
        var parameters = new List<SubCommandParameterBase>();
        var parametersArray = parametersPattern.Split(',');
        foreach (var parameterPattern in parametersArray)
        {
            parameters.Add(GetParameter(parameterPattern));
        }

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", arguments, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.True(subCommand.Executed);
        foreach (var parameterPattern in parametersArray.Where(x => x.StartsWith("required")))
        {
            Assert.True(subCommand.Parameters.ContainsKey(parameterPattern));
        }
        Assert.Equal(expectedParameterValue, subCommand.Parameters[expectedParameterName].GetValue());
    }

    [Theory]
    [InlineData("required-long-prefix-w,optional-int-prefix-x,required-float,required-string", "[Test] " + CommandManager.CommandPrefix + "test <required-float> <required-string> <required-long-prefix-w> [optional-int-prefix-x]")]
    [InlineData("required-float-prefix-yaw,optional-int-prefix-x,required-byte,optional-string", "[Test] " + CommandManager.CommandPrefix + "test <required-byte> [optional-string] <required-float-prefix-yaw> [optional-int-prefix-x]")]
    public void SendHelpMessage_MixedParameters_ShouldSendMessage(string parametersPattern, string expectedCallExample)
    {
        // Arrange
        var parameters = new List<SubCommandParameterBase>();
        var parametersArray = parametersPattern.Split(',');
        foreach (var parameterPattern in parametersArray)
        {
            parameters.Add(GetParameter(parameterPattern));
        }

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.BaseSendHelpMessage(new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        mockCharacter.Verify(c => c.SendMessage(ChatType.System, It.Is<string>(call => call.Contains(expectedCallExample)), It.IsAny<Color?>()), Times.Once);
    }

    [Theory]
    [InlineData("[Test] " + CommandManager.CommandPrefix + "test <a||b||c>", "a", "b", "c")]
    [InlineData("[Test] " + CommandManager.CommandPrefix + "test <a||b>", "a", "b")]
    [InlineData("[Test] " + CommandManager.CommandPrefix + "test <a>", "a")]
    [InlineData("[Test] " + CommandManager.CommandPrefix + "test <test display name>")]
    public void SendHelpMessage_StringValidValuesHelpMessage_ShouldSendMessage(string expectedCallExample, params string[] validValues)
    {
        // Arrange
        var parameters = new List<SubCommandParameterBase>() { new StringSubCommandParameter("test", "test display name", true, validValues) };

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.BaseSendHelpMessage(new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        mockCharacter.Verify(c => c.SendMessage(ChatType.System, It.Is<string>(call => call.Contains(expectedCallExample)), It.IsAny<Color?>()), Times.Once);
    }

    [Theory]
    [InlineData("required-long-prefix-w,optional-int-prefix-x-default-(int)10,required-float,required-string", "optional-int-prefix-x-default-(int)10", 10, "w=100", "-13.23", "requiredstring", "extrastring")]
    [InlineData("optional-int-prefix-x-default-(float)50.93,required-string,optional-string-default-ok", "optional-string-default-ok", "ok", "anything")]
    [InlineData("optional-int-prefix-x-default-(float)50.93,required-string,optional-string-default-ok", "optional-int-prefix-x-default-(float)50.93", 50.93F, "anything")]
    [InlineData("optional-int-prefix-x-default-(uint)50,required-string,optional-string-default-ok", "optional-int-prefix-x-default-(uint)50", 50U, "anything")]
    [InlineData("optional-int-prefix-x-default-(byte)250,required-string,optional-string-default-ok", "optional-int-prefix-x-default-(byte)250", (byte)250, "anything")]
    [InlineData("optional-int-prefix-x-default-(long)1000000,required-string,optional-string-default-ok", "optional-int-prefix-x-default-(long)1000000", 1000000L, "anything")]
    public void LoadParameter_WhenOptionalDefaultParametersAreNotProvided_ShouldDefaultTheValues(string parametersPattern, string expectedParameterName, object expectedParameterDefaultValue, params string[] arguments)
    {
        // Arrange
        var parameters = new List<SubCommandParameterBase>();
        var parametersArray = parametersPattern.Split(',');
        foreach (var parameterPattern in parametersArray)
        {
            parameters.Add(GetParameter(parameterPattern));
        }

        var subCommand = new SubCommandFake(parameters);
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", arguments, new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        Assert.True(subCommand.Executed);
        Assert.Equal(expectedParameterDefaultValue, subCommand.Parameters[expectedParameterName].GetValue());
    }

    private static SubCommandParameterBase GetParameter(string parameterPattern)
    {
        var parameterConfigArray = parameterPattern.Split('-');
        var isRequired = parameterConfigArray.First() == "required";
        var isPrefix = parameterConfigArray.Any(x => x == "prefix");
        var isDefault = parameterConfigArray.Any(x => x == "default");
        var type = parameterConfigArray[1].ToLower();
        string prefixValue = null;
        object defaultValue = null;
        if (isPrefix)
        {
            prefixValue = parameterConfigArray.SkipWhile(s => s != "prefix").Skip(1).First();
        }
        if (isDefault)
        {
            var defaultValueText = parameterConfigArray.SkipWhile(s => s != "default").Skip(1).First();
            if (defaultValueText.StartsWith("(int)"))
            {
                defaultValue = int.Parse(defaultValueText.Replace("(int)", string.Empty));
            }
            else if (defaultValueText.StartsWith("(float)"))
            {
                defaultValue = float.Parse(defaultValueText.Replace("(float)", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture);
            }
            else if (defaultValueText.StartsWith("(double)"))
            {
                defaultValue = double.Parse(defaultValueText.Replace("(double)", string.Empty), NumberStyles.Number, CultureInfo.InvariantCulture);
            }
            else if (defaultValueText.StartsWith("(uint)"))
            {
                defaultValue = uint.Parse(defaultValueText.Replace("(uint)", string.Empty));
            }
            else if (defaultValueText.StartsWith("(long)"))
            {
                defaultValue = long.Parse(defaultValueText.Replace("(long)", string.Empty));
            }
            else if (defaultValueText.StartsWith("(byte)"))
            {
                defaultValue = byte.Parse(defaultValueText.Replace("(byte)", string.Empty));
            }
            else
            {
                defaultValue = defaultValueText;
            }
        }

        return type switch
        {
            "int" => new NumericSubCommandParameter<int>(parameterPattern, null, isRequired, prefixValue) { DefaultValue = defaultValue },
            "uint" => new NumericSubCommandParameter<uint>(parameterPattern, null, isRequired, prefixValue) { DefaultValue = defaultValue },
            "long" => new NumericSubCommandParameter<long>(parameterPattern, null, isRequired, prefixValue) { DefaultValue = defaultValue },
            "float" => new NumericSubCommandParameter<float>(parameterPattern, null, isRequired, prefixValue) { DefaultValue = defaultValue },
            "byte" => new NumericSubCommandParameter<byte>(parameterPattern, null, isRequired, prefixValue) { DefaultValue = defaultValue },
            _ => new StringSubCommandParameter(parameterPattern, null, isRequired, prefixValue) { DefaultValue = defaultValue }
        };
    }
    private static bool AnyNonPrefixParameterMatchInOrder(string[] requiredParametersArray, string[] optionalParametersArray, int nonPrefixCounter)
    {
        return nonPrefixCounter < optionalParametersArray.Length + requiredParametersArray.Length;
    }

    private static bool AnyPrefixedParameterThatMatchesThePrefixedArgument(List<SubCommandParameterBase> parameters, string argument)
    {
        return parameters.Any(p => p.Prefix is not null && p.Prefix == argument.Split('=')[0]);
    }

    private static bool IsPrefixed(string argument)
    {
        return argument.IndexOf('=') > -1;
    }
}
