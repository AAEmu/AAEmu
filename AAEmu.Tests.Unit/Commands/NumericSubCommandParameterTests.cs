#pragma warning disable CS0618 // Type or member is obsolete
using System;
using System.Reflection;
using AAEmu.Game.Utils.Scripts.SubCommands;
using Xunit;

namespace AAEmu.Tests.Unit.Commands
{
    public class NumericSubCommandParameterTests
    {
        [Theory]
        [InlineData(typeof(int), "123", 123)]
        [InlineData(typeof(int), "-123", -123)]
        [InlineData(typeof(uint), "123", 123U)]
        [InlineData(typeof(float), "123", 123F)]
        [InlineData(typeof(float), "123.23", 123.23F)]
        [InlineData(typeof(float), "-123.23", -123.23F)]
        [InlineData(typeof(long), "123", 123L)]
        [InlineData(typeof(long), "-123", -123L)]
        [InlineData(typeof(byte), "123", (byte)123)]
        public void Load_WhenTypeIsSupportedAndValueIsValid_ShouldReturnAsExpected(Type type, string argumentValue, object expectedReturn)
        {
            // Arrange
            Type classType = typeof(NumericSubCommandParameter<>);
            Type constructedType = classType.MakeGenericType(type);
            var parameter = Activator.CreateInstance(constructedType, "numericParameter", "numeric Parameter", true);
            var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

            // Act
            var parameterValue = ((SubCommandParameterBase)parameter).Load(argumentValue);

            // Assert
            Assert.True(parameterValue.IsValid);
            Assert.IsAssignableFrom(expectedParamClass, parameterValue);
            Assert.IsType(type, parameterValue.Value.GetValue());
            Assert.Equal(expectedReturn, parameterValue.Value.GetValue());
        }

        [Theory]
        [InlineData(typeof(string))]
        [InlineData(typeof(decimal))]
        [InlineData(typeof(object))]
        public void Load_WhenTypeIsNotSupported_ShouldReturnUnsupportedMessage(Type type)
        {
            // Arrange
            Type classType = typeof(NumericSubCommandParameter<>);
            Type constructedType = classType.MakeGenericType(type);
            var parameter = Activator.CreateInstance(constructedType, "unsupportedParameter", "unsupported Parameter", true);
            var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

            // Act
            var parameterValue = ((SubCommandParameterBase)parameter).Load("invalidType");

            // Assert
            Assert.IsAssignableFrom(expectedParamClass, parameterValue);
            Assert.False(parameterValue.IsValid);
            Assert.Contains($"Unsupported numeric type {type.Name} for parameter: {((SubCommandParameterBase)parameter).DisplayName}", parameterValue.InvalidMessage);
        }

        [Theory]
        [InlineData(typeof(int), "123.23")]
        [InlineData(typeof(int), "invalid")]
        [InlineData(typeof(uint), "123.23")]
        [InlineData(typeof(uint), "-123.23")]
        [InlineData(typeof(uint), "invalid")]
        [InlineData(typeof(float), "invalid")]
        [InlineData(typeof(long), "123.23")]
        [InlineData(typeof(long), "invalid")]
        [InlineData(typeof(byte), "123.23")]
        [InlineData(typeof(byte), "invalid")]
        public void Load_WhenValueIsInvalid_ShouldReturnInvalidMessage(Type type, string argumentValue)
        {
            // Arrange
            Type classType = typeof(NumericSubCommandParameter<>);
            Type constructedType = classType.MakeGenericType(type);
            var parameter = Activator.CreateInstance(constructedType, "invalidValueParameter", "invalid Value Parameter", true);
            var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

            // Act
            var parameterValue = ((SubCommandParameterBase)parameter).Load(argumentValue);

            // Assert
            Assert.IsAssignableFrom(expectedParamClass, parameterValue);
            Assert.False(parameterValue.IsValid);
            Assert.Contains($"Invalid numeric value for parameter: {((SubCommandParameterBase)parameter).DisplayName}", parameterValue.InvalidMessage);
        }

        [Theory]
        [InlineData(typeof(int), "123", 1, 200, 123)]
        [InlineData(typeof(uint), "123", 1U, 200U, 123U)]
        [InlineData(typeof(float), "599.823", 23F, 690F, 599.823F)]
        [InlineData(typeof(float), "599.823", 599.823F, 599.823F, 599.823F)]
        [InlineData(typeof(float), "599.823", 599.821F, 599.823F, 599.823F)]
        [InlineData(typeof(float), "599.823", 599.823F, 599.824F, 599.823F)]
        [InlineData(typeof(long), "123", 10L, 123L, 123L)]
        [InlineData(typeof(byte), "123", (byte)1, (byte)143, (byte)123)]
        public void Load_WhenValueRangeIsValid_ShouldReturnAsExpected(Type type, string argumentValue, object minValue, object maxValue, object expectedReturn)
        {
            // Arrange
            Type classType = typeof(NumericSubCommandParameter<>);
            Type constructedType = classType.MakeGenericType(type);
            var parameter = Activator.CreateInstance(constructedType, "validRangeParameter", "valid Range Parameter", true, minValue, maxValue);
            var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

            // Act
            var parameterValue = ((SubCommandParameterBase)parameter).Load(argumentValue);

            // Assert
            Assert.True(parameterValue.IsValid);
            Assert.IsAssignableFrom(expectedParamClass, parameterValue);
            Assert.IsType(type, parameterValue.Value.GetValue());
            Assert.Equal(expectedReturn, parameterValue.Value.GetValue());
        }

        [Theory]
        [InlineData(typeof(int), 1240, 125)]
        [InlineData(typeof(uint), 1000U, 200U)]
        [InlineData(typeof(float), 2300F, 690F)]
        [InlineData(typeof(long), 1000L, 123L)]
        [InlineData(typeof(byte), (byte)200, (byte)143)]
        public void Load_WhenConfigRangeIsInvalid_ShouldThrowException(Type type, object minValue, object maxValue)
        {
            // Arrange
            Type classType = typeof(NumericSubCommandParameter<>);
            Type constructedType = classType.MakeGenericType(type);
            var displayName = "invalid Config Range Parameter";
            // Act & Assert
            var exception = Assert.Throws<TargetInvocationException>(() => Activator.CreateInstance(constructedType, "invalidConfigRangeParameter", displayName, true, minValue, maxValue));
            Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
            Assert.Contains($"Parameter [{displayName}] minimum value {minValue} must be less than or equal to maximum value", exception.InnerException.Message);
        }

        [Theory]
        [InlineData(typeof(int), "123", 120, 122)]
        [InlineData(typeof(int), "123", 220, 1122)]
        [InlineData(typeof(uint), "123", 1U, 100U)]
        [InlineData(typeof(uint), "123", 124U, 1000U)]
        [InlineData(typeof(float), "599.823", 23F, 500F)]
        [InlineData(typeof(float), "599.823", 600F, 1000F)]
        [InlineData(typeof(float), "599.823", -599.821F, 598.823F)]
        [InlineData(typeof(long), "123", 10L, 120L)]
        [InlineData(typeof(long), "123", 130L, 220L)]
        [InlineData(typeof(byte), "123", (byte)1, (byte)120)]
        [InlineData(typeof(byte), "123", (byte)124, (byte)220)]
        public void Load_WhenValueRangeIsInvalid_ShouldReturnInvalidMessage(Type type, string argumentValue, object minValue, object maxValue)
        {
            // Arrange
            Type classType = typeof(NumericSubCommandParameter<>);
            Type constructedType = classType.MakeGenericType(type);
            var parameter = Activator.CreateInstance(constructedType, "invalidRangeValueParameter", "invalid Range Value Parameter", true, minValue, maxValue);
            var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

            // Act
            var parameterValue = ((SubCommandParameterBase)parameter).Load(argumentValue);

            // Assert
            Assert.IsAssignableFrom(expectedParamClass, parameterValue);
            Assert.False(parameterValue.IsValid);
            Assert.Contains($"should be between {minValue} and {maxValue} for parameter: {((SubCommandParameterBase)parameter).DisplayName}", parameterValue.InvalidMessage);
        }

        [Theory]
        [InlineData(typeof(int), "prefix", "prefix=123", 123)]
        [InlineData(typeof(int), "a", "a=-123", -123)]
        [InlineData(typeof(uint), "pre", "pre=123", 123U)]
        [InlineData(typeof(float), "fix", "fix=123", 123F)]
        [InlineData(typeof(float), "p", "p=123.23", 123.23F)]
        [InlineData(typeof(float), "xr", "xr=-123.23", -123.23F)]
        [InlineData(typeof(long), "yaw", "yaw=123", 123L)]
        [InlineData(typeof(long), "Z", "Z=-123", -123L)]
        [InlineData(typeof(byte), "sdaf", "sdaf=123", (byte)123)]
        public void Load_WhenPrefixNumericParameterIsValid_ShouldReturnAsExpected(Type type, string prefix, string argumentValue, object expectedReturn)
        {
            // Arrange
            Type classType = typeof(NumericSubCommandParameter<>);
            Type constructedType = classType.MakeGenericType(type);
            var parameter = Activator.CreateInstance(constructedType, "numericParameter", null, true, prefix);
            var expectedParamClass = typeof(ParameterResult<>).MakeGenericType(type);

            // Act
            var parameterResult = ((SubCommandParameterBase)parameter).Load(argumentValue);

            // Assert
            Assert.True(parameterResult.IsValid);
            Assert.IsAssignableFrom(expectedParamClass, parameterResult);
            Assert.IsType(type, parameterResult.Value.GetValue());
            Assert.Equal(expectedReturn, parameterResult.Value.GetValue());
        }
    }
}
