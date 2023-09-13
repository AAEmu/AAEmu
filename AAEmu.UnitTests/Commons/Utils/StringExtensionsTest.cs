using System;
using System.Threading.Tasks;
using AAEmu.Commons.Utils;
using Xunit;

namespace AAEmu.UnitTests.Commons.Utils
{
    public class StringExtensionsTest
    {

        [Theory]
        [InlineData("test", "Test"),
        InlineData("Test", "Test"),
        InlineData("tEST", "TEST"),
        InlineData("TEST", "TEST"),
        InlineData("test test", "Test test"),
        InlineData("t", "T"),
        InlineData("T", "T"),
        InlineData("1", "1")]
        public Task FirstCharToUpper_ShouldWorkAsExpected(string input, string expected)
        {
            // Act
            var actual = input.FirstCharToUpper();

            // Assert
            Assert.Equal(expected, actual);
            return Task.CompletedTask;
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public Task FirstCharToUpper_ShouldThrowWhenInvalid(string input)
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(input.FirstCharToUpper);

            return Task.CompletedTask;
        }
    }
}
