#pragma warning disable CS0618 // Type or member is obsolete

using System.Reflection;
using AAEmu.Game.Utils.Scripts.SubCommands;

namespace AAEmu.UnitTests.Game.Utils.Scripts.SubCommands;

public class NumericSubCommandParameterTests
{
    [Test]
    [Arguments(typeof(int), "123", 123)]
    [Arguments(typeof(int), "-123", -123)]
    [Arguments(typeof(uint), "123", 123U)]
    [Arguments(typeof(float), "123", 123F)]
    [Arguments(typeof(float), "123.23", 123.23F)]
    [Arguments(typeof(float), "-123.23", -123.23F)]
    [Arguments(typeof(long), "123", 123L)]
    [Arguments(typeof(long), "-123", -123L)]
    [Arguments(typeof(byte), "123", (byte)123)]
    public async Task Load_WhenTypeIsSupportedAndValueIsValid_ShouldReturnAsExpected(Type type, string argumentValue, object expectedReturn)
    {
        // Arrange
        var classType = typeof(NumericSubCommandParameter<>);
        var constructedType = classType.MakeGenericType(type);
        var parameter = Activator.CreateInstance(constructedType, "numericParameter", "numeric Parameter", true);
        var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

        // Act
        var parameterValue = ((SubCommandParameterBase)parameter).Load(argumentValue);

        // Assert
        await Assert.That(parameterValue.IsValid).IsTrue();
        await Assert.That(expectedParamClass.IsInstanceOfType(parameterValue)).IsTrue();
        await Assert.That(parameterValue.Value.GetValue().GetType()).IsEqualTo(type);
        await Assert.That(parameterValue.Value.GetValue()).IsEqualTo(expectedReturn);
    }

    [Test]
    [Arguments(typeof(string))]
    [Arguments(typeof(decimal))]
    [Arguments(typeof(object))]
    public async Task Load_WhenTypeIsNotSupported_ShouldReturnUnsupportedMessage(Type type)
    {
        // Arrange
        var classType = typeof(NumericSubCommandParameter<>);
        var constructedType = classType.MakeGenericType(type);
        var parameter = Activator.CreateInstance(constructedType, "unsupportedParameter", "unsupported Parameter", true);
        var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

        // Act
        var parameterValue = ((SubCommandParameterBase)parameter).Load("invalidType");

        // Assert
        await Assert.That(expectedParamClass.IsInstanceOfType(parameterValue)).IsTrue();
        await Assert.That(parameterValue.IsValid).IsFalse();
        await Assert.That(parameterValue.InvalidMessage).Contains($"Unsupported numeric type {type.Name} for parameter: {((SubCommandParameterBase)parameter).DisplayName}");
    }

    [Test]
    [Arguments(typeof(int), "123.23")]
    [Arguments(typeof(int), "invalid")]
    [Arguments(typeof(uint), "123.23")]
    [Arguments(typeof(uint), "-123.23")]
    [Arguments(typeof(uint), "invalid")]
    [Arguments(typeof(float), "invalid")]
    [Arguments(typeof(long), "123.23")]
    [Arguments(typeof(long), "invalid")]
    [Arguments(typeof(byte), "123.23")]
    [Arguments(typeof(byte), "invalid")]
    public async Task Load_WhenValueIsInvalid_ShouldReturnInvalidMessage(Type type, string argumentValue)
    {
        // Arrange
        var classType = typeof(NumericSubCommandParameter<>);
        var constructedType = classType.MakeGenericType(type);
        var parameter = Activator.CreateInstance(constructedType, "invalidValueParameter", "invalid Value Parameter", true);
        var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

        // Act
        var parameterValue = ((SubCommandParameterBase)parameter).Load(argumentValue);

        // Assert
        await Assert.That(expectedParamClass.IsInstanceOfType(parameterValue)).IsTrue();
        await Assert.That(parameterValue.IsValid).IsFalse();
        await Assert.That(parameterValue.InvalidMessage).Contains($"Invalid numeric value for parameter: {((SubCommandParameterBase)parameter).DisplayName}");
    }

    [Test]
    [Arguments(typeof(int), "123", 1, 200, 123)]
    [Arguments(typeof(uint), "123", 1U, 200U, 123U)]
    [Arguments(typeof(float), "599.823", 23F, 690F, 599.823F)]
    [Arguments(typeof(float), "599.823", 599.823F, 599.823F, 599.823F)]
    [Arguments(typeof(float), "599.823", 599.821F, 599.823F, 599.823F)]
    [Arguments(typeof(float), "599.823", 599.823F, 599.824F, 599.823F)]
    [Arguments(typeof(long), "123", 10L, 123L, 123L)]
    [Arguments(typeof(byte), "123", (byte)1, (byte)143, (byte)123)]
    public async Task Load_WhenValueRangeIsValid_ShouldReturnAsExpected(Type type, string argumentValue, object minValue, object maxValue, object expectedReturn)
    {
        // Arrange
        var classType = typeof(NumericSubCommandParameter<>);
        var constructedType = classType.MakeGenericType(type);
        var parameter = Activator.CreateInstance(constructedType, "validRangeParameter", "valid Range Parameter", true, minValue, maxValue);
        var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

        // Act
        var parameterValue = ((SubCommandParameterBase)parameter).Load(argumentValue);

        // Assert
        await Assert.That(parameterValue.IsValid).IsTrue();
        await Assert.That(expectedParamClass.IsInstanceOfType(parameterValue)).IsTrue();
        await Assert.That(parameterValue.Value.GetValue().GetType()).IsEqualTo(type);
        await Assert.That(parameterValue.Value.GetValue()).IsEqualTo(expectedReturn);
    }

    [Test]
    [Arguments(typeof(int), 1240, 125)]
    [Arguments(typeof(uint), 1000U, 200U)]
    [Arguments(typeof(float), 2300F, 690F)]
    [Arguments(typeof(long), 1000L, 123L)]
    [Arguments(typeof(byte), (byte)200, (byte)143)]
    public async Task Load_WhenConfigRangeIsInvalid_ShouldThrowException(Type type, object minValue, object maxValue)
    {
        // Arrange
        var classType = typeof(NumericSubCommandParameter<>);
        var constructedType = classType.MakeGenericType(type);
        var displayName = "invalid Config Range Parameter";
        // Act & Assert
        var exception = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(constructedType, "invalidConfigRangeParameter", displayName, true, minValue, maxValue));
        await Assert.That(exception.InnerException).IsTypeOf<ArgumentOutOfRangeException>();
        await Assert.That(exception.InnerException!.Message).Contains($"Parameter [{displayName}] minimum value {minValue} must be less than or equal to maximum value");
    }

    [Test]
    [Arguments(typeof(int), "123", 120, 122)]
    [Arguments(typeof(int), "123", 220, 1122)]
    [Arguments(typeof(uint), "123", 1U, 100U)]
    [Arguments(typeof(uint), "123", 124U, 1000U)]
    [Arguments(typeof(float), "599.823", 23F, 500F)]
    [Arguments(typeof(float), "599.823", 600F, 1000F)]
    [Arguments(typeof(float), "599.823", -599.821F, 598.823F)]
    [Arguments(typeof(long), "123", 10L, 120L)]
    [Arguments(typeof(long), "123", 130L, 220L)]
    [Arguments(typeof(byte), "123", (byte)1, (byte)120)]
    [Arguments(typeof(byte), "123", (byte)124, (byte)220)]
    public async Task Load_WhenValueRangeIsInvalid_ShouldReturnInvalidMessage(Type type, string argumentValue, object minValue, object maxValue)
    {
        // Arrange
        var classType = typeof(NumericSubCommandParameter<>);
        var constructedType = classType.MakeGenericType(type);
        var parameter = Activator.CreateInstance(constructedType, "invalidRangeValueParameter", "invalid Range Value Parameter", true, minValue, maxValue);
        var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

        // Act
        var parameterValue = ((SubCommandParameterBase)parameter).Load(argumentValue);

        // Assert
        await Assert.That(expectedParamClass.IsInstanceOfType(parameterValue)).IsTrue();
        await Assert.That(parameterValue.IsValid).IsFalse();
        await Assert.That(parameterValue.InvalidMessage).Contains($"should be between {minValue} and {maxValue} for parameter: {((SubCommandParameterBase)parameter).DisplayName}");
    }

    [Test]
    [Arguments(typeof(int), "prefix", "prefix=123", 123)]
    [Arguments(typeof(int), "a", "a=-123", -123)]
    [Arguments(typeof(uint), "pre", "pre=123", 123U)]
    [Arguments(typeof(float), "fix", "fix=123", 123F)]
    [Arguments(typeof(float), "p", "p=123.23", 123.23F)]
    [Arguments(typeof(float), "xr", "xr=-123.23", -123.23F)]
    [Arguments(typeof(long), "yaw", "yaw=123", 123L)]
    [Arguments(typeof(long), "Z", "Z=-123", -123L)]
    [Arguments(typeof(byte), "sdaf", "sdaf=123", (byte)123)]
    public async Task Load_WhenPrefixNumericParameterIsValid_ShouldReturnAsExpected(Type type, string prefix, string argumentValue, object expectedReturn)
    {
        // Arrange
        var classType = typeof(NumericSubCommandParameter<>);
        var constructedType = classType.MakeGenericType(type);
        var parameter = Activator.CreateInstance(constructedType, "numericParameter", null, true, prefix);
        var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

        // Act
        var parameterResult = ((SubCommandParameterBase)parameter).Load(argumentValue);

        // Assert
        await Assert.That(parameterResult.IsValid).IsTrue();
        await Assert.That(expectedParamClass.IsInstanceOfType(parameterResult)).IsTrue();
        await Assert.That(parameterResult.Value.GetValue().GetType()).IsEqualTo(type);
        await Assert.That(parameterResult.Value.GetValue()).IsEqualTo(expectedReturn);
    }
}