#pragma warning disable CS0618 // Type or member is obsolete

using System.Drawing;
using System.Globalization;
using AAEmu.Game.Core.Managers;
using AAEmu.Game.Models.Game.Char;
using AAEmu.Game.Models.Game.Chat;
using AAEmu.Game.Utils.Scripts;
using AAEmu.Game.Utils.Scripts.SubCommands;
using Moq;

namespace AAEmu.UnitTests.Game.Utils.Scripts.SubCommands;

public class SubCommandBaseTests
{
    [Test]
    [Arguments("param1,param2", "test")]
    [Arguments("param1,param2,param3,param4", "test")]
    [Arguments("param1,param2,param3,param4", "test", "test2", "test3")]
    public async Task PreValidate_WhenRequiredStringParametersAreNotMet_PreValidateShouldSendMessage(string requiredParameters, params string[] arguments)
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
        await Assert.That(subCommand.Executed).IsFalse();
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

    [Test]
    [Arguments("required-prefix-x", "x=test", "x=test2duplicate")]
    [Arguments("optional-prefix-y", "y=test", "y=test2duplicate")]
    [Arguments("optional-prefix-name", "y=test", "y=test2duplicate", "name=ok", "name=duplicate")]
    public async Task PreValidate_WhenDuplicatedPrefixParameters_PreValidateShouldSendMessage(string prefixParameters, params string[] arguments)
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
        await Assert.That(subCommand.Executed).IsFalse();

        // TODO: Fix this test
        // mockCharacter.Verify(c => c.SendMessage(It.IsIn(ChatType.System), It.IsIn($"[Test] Parameter prefix {parameters[0].Prefix} is duplicated"), It.IsIn(Color.Red)), Times.Once);
    }

    [Test]
    [Arguments("param1,param2", "test", "test2")]
    [Arguments("param1,param2,param3,param4", "test", "test2", "test3", "test4")]
    public async Task PreValidate_WhenRequiredStringParametersAreMet_ShouldExecuteWithParameters(string requiredParameters, params string[] arguments)
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
        await Assert.That(subCommand.Executed).IsTrue();
        await Assert.That(subCommand.Parameters.Count).IsEqualTo(arguments.Length);
        var counter = 0;
        foreach (var parameterKeyValue in subCommand.Parameters)
        {
            await Assert.That(parameterKeyValue.Key).IsEqualTo(parameters[counter].Name);
            await Assert.That(parameterKeyValue.Value).IsEqualTo(arguments[counter]);
            counter++;
        }
        mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsAny<string>(), It.IsAny<Color>()), Times.Never);
    }

    public static IEnumerable<(string, string, string[])> OptionalParameterNotPresentData() =>
    [
        ("param1", "param2", ["test"]),
        ("param1,param2", "param3,param4", ["test", "test2"]),
        ("param1,param2", "param3,param4", ["test", "test2", "test3"]),
        ("param1,param2", "param3,param4", ["test", "test2", "test3", "test4"]),
        ("param1,param2", "param3,param4", ["test", "test2", "test3", "test4", "test5"]),
        ("param1,param2", "param3,param4,param5", ["test", "test2", "test3", "test4", "test5"]),
        ("param1,param2", "param3,param4,param5", ["test", "test2", "test3", "test4", "test5", "test6", "test7"]),
    ];

    [Test]
    [MethodDataSource(nameof(OptionalParameterNotPresentData))]
    public async Task LoadParametersValues_WhenOptionalParameterAreNotPresent_ShouldExecuteWithOnlyProvidedParameters(string requiredParameters, string optionalParameters, string[] arguments)
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
        await Assert.That(subCommand.Executed).IsTrue();

        await Assert.That(subCommand.Parameters.Count).IsEqualTo(expectedNumberOfParameters);
        var counter = 0;
        foreach (var parameterKeyValue in subCommand.Parameters)
        {
            await Assert.That(parameterKeyValue.Key).IsEqualTo(parameters[counter].Name);
            await Assert.That(parameterKeyValue.Value).IsEqualTo(arguments[counter]);
            counter++;
        }
        mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsAny<string>(), It.IsAny<Color>()), Times.Never);
    }

    [Test]
    [Arguments("test", "valid1", "valid2")]
    [Arguments("test", "test1")]
    public async Task PreValidate_WhenRangedStringParameterAreNotMet_PreValidateShouldSendMessage(string argumentValue, params string[] validValues)
    {
        // Arrange
        var parameter = new StringSubCommandParameter("param1", "parameter 1", true, validValues);
        var subCommand = new SubCommandFake(new[] { parameter });
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", [argumentValue], new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        await Assert.That(subCommand.Executed).IsFalse();

        // TODO: fix this test
        // mockCharacter.Verify(c => c.SendMessage(It.IsIn<ChatType>(), It.IsIn($"[Test] Parameter [{parameter.DisplayName}] only accepts: {string.Join("||", validValues)}"), It.IsIn(Color.Red)), Times.Once);
    }

    [Test]
    [Arguments("test", "test", "test2", "test3")]
    public async Task PreValidate_WhenRangedStringParameterAreMet_ShouldExecute(string argumentValue, params string[] validValues)
    {
        // Arrange
        var parameter = new StringSubCommandParameter("param1", "parameter 1", true, validValues);
        var subCommand = new SubCommandFake(new[] { parameter });
        var mockCharacter = new Mock<ICharacter>();

        // Act
        subCommand.PreExecute(mockCharacter.Object, "", [argumentValue], new CharacterMessageOutput(mockCharacter.Object));

        // Assert
        await Assert.That(subCommand.Executed).IsTrue();
        await Assert.That(subCommand.Parameters).HasSingleItem();
        await Assert.That(subCommand.Parameters["param1"]).IsEqualTo(argumentValue);

        mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsAny<string>(), It.IsAny<Color>()), Times.Never);
    }

    public static IEnumerable<(string, string, string, string[])> MixedNonPrefixAndAnyOrderPrefixData() =>
    [
        ("req1", "opt2", "required-prefix-x", ["x=test", "firstRequired", "SecondOptional", "shouldIgnoreMe"]),
        ("req1", "opt2", "required-prefix-x", ["firstRequired", "x=test", "SecondOptional", "shouldIgnoreMe", "shouldIgnoreMe"]),
        ("req1", "opt2", "required-prefix-x", ["firstRequired", "SecondOptional", "x=test", "shouldIgnoreMe", "shouldIgnoreMe", "y=z"]),
        ("req1", "opt2", "required-prefix-x", ["firstRequired", "SecondOptional", "x=test", "shouldIgnoreMe", "shouldIgnoreMe", "y=z", "ignoreMe"]),
    ];

    [Test]
    [MethodDataSource(nameof(MixedNonPrefixAndAnyOrderPrefixData))]
    public async Task LoadParametersValues_WhenMixedNonPrefixAndAnyOrderPrefix_ShouldExecute(string requiredParameters, string optionalParameters, string prefixParameters, string[] arguments)
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
        await Assert.That(subCommand.Executed).IsTrue();

        await Assert.That(subCommand.Parameters.Count).IsEqualTo(3);

        var counter = 0;
        var nonPrefixCounter = 0;
        foreach (var argument in arguments)
        {
            if (IsPrefixed(argument))
            {
                if (AnyPrefixedParameterThatMatchesThePrefixedArgument(parameters, argument))
                {
                    // Any parameters configured as prefix where the argument matches should be present in the subcommand parameters
                    await Assert.That(subCommand.Parameters).Contains(p => p.Value.ToString() == argument.Split('=')[1]);
                }
                else
                {
                    // Any parameters that are not by prefix should not have prefixed argument values
                    await Assert.That(subCommand.Parameters).DoesNotContain(p => p.Value.ToString() == argument.Split('=')[1]);
                }
            }
            else
            {
                if (AnyNonPrefixParameterMatchInOrder(requiredParametersArray, optionalParametersArray, nonPrefixCounter))
                {
                    // Any argument that not match the prefix pattern should be matching the non prefix parameters in order
                    // If the nonprefix arguments matched more the number of nonprefix parameters we don't expect the parameters to contains any of those values
                    await Assert.That(subCommand.Parameters).Contains(p => p.Value.ToString() == argument);
                    nonPrefixCounter++;
                }
            }
            counter++;
        }
        mockCharacter.Verify(c => c.SendMessage(It.IsAny<ChatType>(), It.IsAny<string>(), It.IsAny<Color>()), Times.Never);
    }

    [Test]
    [Arguments("required-long-prefix-w,optional-int-prefix-x,required-float,required-string", "required-float", -13.23F, "x=12", "w=100", "-13.23", "requiredstring", "extrastring")]
    [Arguments("required-long-prefix-w,optional-int-prefix-x,required-float,required-string", "required-float", 123.54F, "w=100", "123.54", "requiredstring", "extrastring")]
    [Arguments("required-float-prefix-yaw,optional-int-prefix-x,required-byte,optional-string", "required-float-prefix-yaw", 185.4323F, "yaw=185.4323", "200")]
    public async Task LoadParametersValues_MixedParameters_ShouldExecute(string parametersPattern, string expectedParameterName, object expectedParameterValue, params string[] arguments)
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
        await Assert.That(subCommand.Executed).IsTrue();
        foreach (var parameterPattern in parametersArray.Where(x => x.StartsWith("required")))
        {
            await Assert.That(subCommand.Parameters.ContainsKey(parameterPattern)).IsTrue();
        }
        await Assert.That(subCommand.Parameters[expectedParameterName].GetValue()).IsEqualTo(expectedParameterValue);
    }

    [Test]
    [Arguments("required-long-prefix-w,optional-int-prefix-x,required-float,required-string", "[Test] " + CommandManager.CommandPrefix + "test <required-float> <required-string> <required-long-prefix-w> [optional-int-prefix-x]")]
    [Arguments("required-float-prefix-yaw,optional-int-prefix-x,required-byte,optional-string", "[Test] " + CommandManager.CommandPrefix + "test <required-byte> [optional-string] <required-float-prefix-yaw> [optional-int-prefix-x]")]
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

    [Test]
    [Arguments("[Test] " + CommandManager.CommandPrefix + "test <a||b||c>", "a", "b", "c")]
    [Arguments("[Test] " + CommandManager.CommandPrefix + "test <a||b>", "a", "b")]
    [Arguments("[Test] " + CommandManager.CommandPrefix + "test <a>", "a")]
    [Arguments("[Test] " + CommandManager.CommandPrefix + "test <test display name>")]
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

    [Test]
    [Arguments("required-long-prefix-w,optional-int-prefix-x-default-(int)10,required-float,required-string", "optional-int-prefix-x-default-(int)10", 10, "w=100", "-13.23", "requiredstring", "extrastring")]
    [Arguments("optional-int-prefix-x-default-(float)50.93,required-string,optional-string-default-ok", "optional-string-default-ok", "ok", "anything")]
    [Arguments("optional-int-prefix-x-default-(float)50.93,required-string,optional-string-default-ok", "optional-int-prefix-x-default-(float)50.93", 50.93F, "anything")]
    [Arguments("optional-int-prefix-x-default-(uint)50,required-string,optional-string-default-ok", "optional-int-prefix-x-default-(uint)50", 50U, "anything")]
    [Arguments("optional-int-prefix-x-default-(byte)250,required-string,optional-string-default-ok", "optional-int-prefix-x-default-(byte)250", (byte)250, "anything")]
    [Arguments("optional-int-prefix-x-default-(long)1000000,required-string,optional-string-default-ok", "optional-int-prefix-x-default-(long)1000000", 1000000L, "anything")]
    public async Task LoadParameter_WhenOptionalDefaultParametersAreNotProvided_ShouldDefaultTheValues(string parametersPattern, string expectedParameterName, object expectedParameterDefaultValue, params string[] arguments)
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
        await Assert.That(subCommand.Executed).IsTrue();
        await Assert.That(subCommand.Parameters[expectedParameterName].GetValue()).IsEqualTo(expectedParameterDefaultValue);
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